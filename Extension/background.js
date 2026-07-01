import { Collector } from './lib/collector.js';
import { Communicator } from './lib/communicator.js';

class BackgroundService {
  constructor() {
    this.collector = new Collector();
    this.communicator = new Communicator();

    this.activeDownloads = new Map();
    this.pendingCancels = new Map();
    this.settings = {
      enabled: true,
      autoIntercept: true,
      maxConnections: 16,
      port: 57575,
      captureHeaders: true,
      captureCookies: true,
      capturePostData: true,
      captureClientHints: true,
      fileTypes: [
        'exe', 'msi', 'dmg', 'apk', 'zip', 'rar', '7z', 'tar', 'gz',
        'pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx',
        'mp4', 'mkv', 'avi', 'mov', 'wmv', 'flv', 'webm',
        'mp3', 'flac', 'wav', 'aac', 'ogg',
        'iso', 'bin', 'cue', 'img'
      ]
    };

    this.init();
  }

  async init() {
    await this.loadSettings();
    this.setupEventListeners();
    this.setupContextMenus();

    const connected = await this.communicator.connect(this.settings.port);
    console.log('[MAC-1] Background service initialized, connected:', connected);

    this.updateBadge();
  }

  async loadSettings() {
    try {
      const stored = await chrome.storage.local.get('settings');
      if (stored.settings) {
        this.settings = { ...this.settings, ...stored.settings };
      }
    } catch (e) {
      console.error('[MAC-1] Failed to load settings:', e);
    }
  }

  async saveSettings() {
    try {
      await chrome.storage.local.set({ settings: this.settings });
    } catch (e) {
      console.error('[MAC-1] Failed to save settings:', e);
    }
  }

  setupEventListeners() {
    chrome.downloads.onCreated.addListener((downloadItem) => {
      this.handleDownloadCreated(downloadItem);
    });

    chrome.downloads.onChanged.addListener((delta) => {
      this.handleDownloadChanged(delta);
    });

    chrome.webRequest.onBeforeSendHeaders.addListener(
      (details) => this.collector.captureRequestHeaders(details),
      { urls: ['<all_urls>'] },
      ['requestHeaders', 'extraHeaders']
    );

    chrome.webRequest.onHeadersReceived.addListener(
      (details) => this.collector.captureResponseHeaders(details),
      { urls: ['<all_urls>'] },
      ['responseHeaders', 'extraHeaders']
    );

    chrome.webRequest.onBeforeRequest.addListener(
      (details) => this.collector.capturePostData(details),
      { urls: ['<all_urls>'] },
      ['requestBody']
    );

    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
      this.handleMessage(message, sender, sendResponse);
      return true;
    });

    chrome.storage.onChanged.addListener((changes) => {
      if (changes.settings) {
        this.settings = { ...this.settings, ...changes.settings.newValue };
      }
    });
  }

  setupContextMenus() {
    chrome.contextMenus.create({
      id: 'mac1-download-link',
      title: 'Download with MAC-1',
      contexts: ['link']
    });

    chrome.contextMenus.create({
      id: 'mac1-download-page',
      title: 'Download all links with MAC-1',
      contexts: ['page']
    });

    chrome.contextMenus.onClicked.addListener((info, tab) => {
      this.handleContextMenu(info, tab);
    });
  }

  async handleDownloadCreated(downloadItem) {
    if (!this.settings.enabled || !this.settings.autoIntercept) {
      return;
    }

    const extension = this.getFileExtension(downloadItem.url);
    if (!this.isDownloadableFileType(extension)) {
      return;
    }

    this.pendingCancels.set(downloadItem.id, {
      url: downloadItem.url,
      filename: downloadItem.filename,
      fileSize: downloadItem.fileSize,
      mime: downloadItem.mime,
      tabId: downloadItem.tabId,
      method: downloadItem.method,
      referrer: downloadItem.referrer,
      incident: downloadItem.incognito,
      timestamp: Date.now()
    });

    this.cancelDownloadFast(downloadItem.id);
  }

  async cancelDownloadFast(downloadId) {
    try {
      await chrome.downloads.cancel(downloadId);
    } catch (e) {
      if (!e.message?.includes('No file found')) {
        console.error('[MAC-1] Cancel error:', e);
      }
    }

    const pending = this.pendingCancels.get(downloadId);
    if (!pending) return;
    this.pendingCancels.delete(downloadId);

    this.processCapturedDownload(downloadId, pending);
  }

  handleDownloadChanged(delta) {
    if (delta.state?.current === 'interrupted' && delta.error?.current) {
      const pending = this.pendingCancels.get(delta.id);
      if (pending) {
        this.pendingCancels.delete(delta.id);
        this.processCapturedDownload(delta.id, pending);
      }
    }
  }

  async processCapturedDownload(downloadId, info) {
    const downloadItem = {
      id: downloadId,
      url: info.url,
      filename: info.filename,
      fileSize: info.fileSize,
      mime: info.mime,
      tabId: info.tabId,
      method: info.method,
      referrer: info.referrer
    };

    try {
      const completeData = await this.collector.collectAllData(downloadItem);
      this.activeDownloads.set(downloadId, completeData);
      this.updateBadge();

      this.communicator.sendDownload(completeData).catch(e => {
        console.error('[MAC-1] Send failed, queuing:', e);
      });

      this.showNotification(
        'Download Captured',
        `${completeData.filename || 'File'} sent to MAC-1`
      );

    } catch (e) {
      console.error('[MAC-1] Failed to process download:', e);

      const fallbackData = {
        url: info.url,
        filename: info.filename || this.extractFilename(info.url),
        fileSize: info.fileSize || 0,
        mimeType: info.mime || '',
        method: info.method || 'GET',
        tabId: info.tabId,
        timestamp: info.timestamp,
        headers: {},
        cookies: [],
        tab: null,
        response: null,
        postData: null,
        clientHints: null,
        referrer: info.referrer || '',
        userAgent: navigator.userAgent,
        platform: navigator.platform
      };

      this.activeDownloads.set(downloadId, fallbackData);
      this.updateBadge();

      this.communicator.sendDownload(fallbackData).catch(() => {});
    }
  }

  getFileExtension(url) {
    try {
      const urlObj = new URL(url);
      const pathParts = urlObj.pathname.split('.');
      if (pathParts.length > 1) {
        return pathParts[pathParts.length - 1].toLowerCase().split('?')[0];
      }
    } catch (e) {}
    return '';
  }

  extractFilename(url) {
    try {
      const urlObj = new URL(url);
      const pathParts = urlObj.pathname.split('/');
      const lastPart = pathParts[pathParts.length - 1];
      if (lastPart && lastPart.includes('.')) {
        return decodeURIComponent(lastPart);
      }
    } catch (e) {}
    return 'download';
  }

  isDownloadableFileType(extension) {
    if (!extension) return false;
    return this.settings.fileTypes.includes(extension.toLowerCase());
  }

  async handleContextMenu(info, tab) {
    if (info.menuItemId === 'mac1-download-link' && info.linkUrl) {
      const data = await this.collector.collectFromTab(tab, info.linkUrl);
      this.activeDownloads.set('ctx-' + Date.now(), data);
      this.updateBadge();
      await this.communicator.sendDownload(data);
    }

    if (info.menuItemId === 'mac1-download-page') {
      const links = await this.getPageLinks(tab);
      for (const link of links) {
        const data = await this.collector.collectFromTab(tab, link);
        await this.communicator.sendDownload(data);
      }
    }
  }

  async getPageLinks(tab) {
    try {
      const results = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => Array.from(document.querySelectorAll('a[href]')).map(a => a.href)
      });
      return results[0]?.result || [];
    } catch (e) {
      return [];
    }
  }

  handleMessage(message, sender, sendResponse) {
    switch (message.type) {
      case 'GET_STATUS':
        sendResponse({
          enabled: this.settings.enabled,
          activeDownloads: this.activeDownloads.size,
          connected: this.communicator.isConnected
        });
        break;

      case 'TOGGLE_ENABLED':
        this.settings.enabled = !this.settings.enabled;
        this.saveSettings();
        this.updateBadge();
        sendResponse({ enabled: this.settings.enabled });
        break;

      case 'DOWNLOAD_URL':
        this.handleManualDownload(message.url, message.tabId);
        sendResponse({ success: true });
        break;

      case 'REDIRECT_DOWNLOAD':
        this.handleRedirectDownload(message.url, sender);
        sendResponse({ success: true });
        break;

      case 'GET_SETTINGS':
        sendResponse({ settings: this.settings });
        break;

      case 'UPDATE_SETTINGS':
        this.settings = { ...this.settings, ...message.settings };
        this.saveSettings();
        sendResponse({ success: true });
        break;

      case 'CHECK_CONNECTION':
        this.checkConnection().then(connected => {
          sendResponse({ connected });
        });
        return true;

      case 'DOWNLOAD_COMPLETE':
        this.activeDownloads.delete(message.downloadId);
        this.updateBadge();
        break;
    }
  }

  async handleRedirectDownload(url, sender) {
    try {
      const data = await this.collector.collectFromUrl(url, sender?.tab);
      this.activeDownloads.set('redirect-' + Date.now(), data);
      this.updateBadge();
      await this.communicator.sendDownload(data);
      this.showNotification('Download Captured', `${data.filename || 'File'} sent to MAC-1`);
    } catch (e) {
      console.error('[MAC-1] Redirect download failed:', e);
    }
  }

  async checkConnection() {
    try {
      await this.communicator.connect(this.settings.port);
      return this.communicator.isConnected;
    } catch (e) {
      return false;
    }
  }

  async handleManualDownload(url, tabId) {
    try {
      let tab = null;
      if (tabId) {
        tab = await chrome.tabs.get(tabId);
      } else {
        const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
        tab = tabs[0];
      }

      const data = await this.collector.collectFromTab(tab, url);
      this.activeDownloads.set('manual-' + Date.now(), data);
      this.updateBadge();
      await this.communicator.sendDownload(data);
      this.showNotification('Download Started', `Sending to MAC-1`);
    } catch (e) {
      console.error('[MAC-1] Manual download failed:', e);
    }
  }

  updateBadge() {
    const count = this.activeDownloads.size;
    chrome.action.setBadgeText({ text: count > 0 ? count.toString() : '' });
    chrome.action.setBadgeBackgroundColor({ color: count > 0 ? '#0077FF' : '#888888' });
  }

  showNotification(title, message) {
    chrome.notifications.create({
      type: 'basic',
      iconUrl: 'icons/icon128.png',
      title: title,
      message: message
    });
  }
}

const backgroundService = new BackgroundService();

import { Collector } from './lib/collector.js';
import { Communicator } from './lib/communicator.js';
import { Interceptor } from './lib/interceptor.js';
import { Utils } from './lib/utils.js';

class BackgroundService {
  constructor() {
    this.collector = new Collector();
    this.communicator = new Communicator();
    this.interceptor = new Interceptor();
    this.utils = new Utils();

    this.activeDownloads = new Map();
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

    const extension = this.utils.getFileExtension(downloadItem.url);
    if (!this.isDownloadableFileType(extension)) {
      return;
    }

    try {
      await chrome.downloads.cancel(downloadItem.id);

      const completeData = await this.collector.collectAllData(downloadItem);
      this.activeDownloads.set(downloadItem.id, completeData);
      this.updateBadge();

      await this.communicator.sendDownload(completeData);

      this.showNotification(
        'Download Captured',
        `${completeData.filename || 'File'} sent to MAC-1`
      );

    } catch (e) {
      console.error('[MAC-1] Failed to handle download:', e);
    }
  }

  isDownloadableFileType(extension) {
    if (!extension) return false;
    return this.settings.fileTypes.includes(extension.toLowerCase());
  }

  async handleContextMenu(info, tab) {
    if (info.menuItemId === 'mac1-download-link' && info.linkUrl) {
      const data = await this.collector.collectFromTab(tab, info.linkUrl);
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
        func: () => {
          const links = document.querySelectorAll('a[href]');
          return Array.from(links).map(a => a.href);
        }
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

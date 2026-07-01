import { Collector } from './lib/collector.js';
import { Communicator } from './lib/communicator.js';

class BackgroundService {
  constructor() {
    this.collector = new Collector();
    this.communicator = new Communicator();

    this.activeDownloads = new Map();
    this.sentUrls = new Set();
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
        'mp4', 'mkv', 'avi', 'mov', 'wmv', 'flv', 'webm', 'm4v',
        'mp3', 'flac', 'wav', 'aac', 'ogg', 'wma',
        'iso', 'bin', 'cue', 'img'
      ]
    };

    this.init();
  }

  async init() {
    await this.loadSettings();
    this.setupEventListeners();
    this.setupContextMenus();
    await this.communicator.connect(this.settings.port);
    this.updateBadge();
  }

  async loadSettings() {
    try {
      const stored = await chrome.storage.local.get('settings');
      if (stored.settings) this.settings = { ...this.settings, ...stored.settings };
    } catch (e) {}
  }

  async saveSettings() {
    try {
      await chrome.storage.local.set({ settings: this.settings });
    } catch (e) {}
  }

  setupEventListeners() {
    chrome.downloads.onCreated.addListener((item) => this.handleDownloadCreated(item));

    chrome.webRequest.onBeforeSendHeaders.addListener(
      (d) => this.collector.captureRequestHeaders(d),
      { urls: ['<all_urls>'] },
      ['requestHeaders', 'extraHeaders']
    );

    chrome.webRequest.onHeadersReceived.addListener(
      (d) => this.collector.captureResponseHeaders(d),
      { urls: ['<all_urls>'] },
      ['responseHeaders', 'extraHeaders']
    );

    chrome.webRequest.onBeforeRequest.addListener(
      (d) => this.collector.capturePostData(d),
      { urls: ['<all_urls>'] },
      ['requestBody']
    );

    chrome.runtime.onMessage.addListener((msg, sender, respond) => {
      this.handleMessage(msg, sender, respond);
      return true;
    });

    chrome.storage.onChanged.addListener((changes) => {
      if (changes.settings) this.settings = { ...this.settings, ...changes.settings.newValue };
    });
  }

  setupContextMenus() {
    chrome.contextMenus.create({ id: 'mac1-dl-link', title: 'Download with MAC-1', contexts: ['link'] });
    chrome.contextMenus.create({ id: 'mac1-dl-page', title: 'Download all links with MAC-1', contexts: ['page'] });
    chrome.contextMenus.onClicked.addListener((info, tab) => this.handleContextMenu(info, tab));
  }

  isDownloadableFileType(ext) {
    return ext && this.settings.fileTypes.includes(ext.toLowerCase());
  }

  getFileExtension(url) {
    try {
      const p = new URL(url).pathname.split('.');
      if (p.length > 1) return p[p.length - 1].toLowerCase().split('?')[0];
    } catch (e) {}
    return '';
  }

  extractFilename(url) {
    try {
      const parts = new URL(url).pathname.split('/');
      const name = parts[parts.length - 1];
      return name && name.includes('.') ? decodeURIComponent(name) : 'download';
    } catch (e) {}
    return 'download';
  }

  hasAlreadySent(url) {
    if (this.sentUrls.has(url)) return true;
    this.sentUrls.add(url);
    setTimeout(() => this.sentUrls.delete(url), 10000);
    return false;
  }

  async sendBasicToService(data) {
    if (this.hasAlreadySent(data.url)) return;

    const session = {
      url: data.url,
      finalUrl: data.url,
      filename: data.filename || this.extractFilename(data.url),
      fileExtension: this.getFileExtension(data.url),
      fileSize: 0,
      mimeType: '',
      referrer: data.tab?.url || '',
      origin: '',
      method: 'GET',
      userAgent: navigator.userAgent,
      platform: navigator.platform,
      initiator: '',
      host: '',
      protocol: '',
      port: 0,
      timestamp: Date.now(),
      downloadSource: 'click-intercept',
      resumeSupported: true,
      savePath: '',
      category: 'General',
      description: '',
      headers: {},
      responseHeaders: null,
      cookies: [],
      clientHints: null,
      postData: null,
      tab: data.tab || null,
      redirectChain: []
    };

    try {
      await this.communicator.sendDownload(session);
      this.showNotification('Download Captured', `${session.filename} sent to MAC-1`);
    } catch (e) {
      console.error('[MAC-1] Failed to send basic session:', e);
    }
  }

  async handleDownloadCreated(downloadItem) {
    if (!this.settings.enabled || !this.settings.autoIntercept) return;

    const ext = this.getFileExtension(downloadItem.url);
    if (!this.isDownloadableFileType(ext)) return;

    this.collector.collectAllData(downloadItem).then(async (completeData) => {
      if (this.hasAlreadySent(completeData.url)) {
        try { await chrome.downloads.cancel(downloadItem.id); } catch (e) {}
        return;
      }

      this.activeDownloads.set(downloadItem.id, completeData);
      this.updateBadge();

      try { await chrome.downloads.cancel(downloadItem.id); } catch (e) {}

      this.communicator.sendDownload(completeData).catch(() => {});

      this.showNotification('Download Captured', `${completeData.filename || 'File'} sent to MAC-1`);
    }).catch(async (e) => {
      console.error('[MAC-1] collectAllData failed:', e);
      try { await chrome.downloads.cancel(downloadItem.id); } catch (err) {}
    });
  }

  async handleContextMenu(info, tab) {
    if (info.menuItemId === 'mac1-dl-link' && info.linkUrl) {
      const data = await this.collector.collectFromTab(tab, info.linkUrl);
      this.activeDownloads.set('ctx-' + Date.now(), data);
      this.updateBadge();
      await this.communicator.sendDownload(data);
    }

    if (info.menuItemId === 'mac1-dl-page') {
      try {
        const results = await chrome.scripting.executeScript({
          target: { tabId: tab.id },
          func: () => Array.from(document.querySelectorAll('a[href]')).map(a => a.href)
        });
        const links = results[0]?.result || [];
        for (const link of links) {
          const ext = this.getFileExtension(link);
          if (this.isDownloadableFileType(ext)) {
            const data = await this.collector.collectFromTab(tab, link);
            await this.communicator.sendDownload(data);
          }
        }
      } catch (e) {}
    }
  }

  handleMessage(message, sender, respond) {
    switch (message.type) {
      case 'INTERCEPT_CLICK':
        this.sendBasicToService(message);
        respond({ success: true });
        break;

      case 'GET_STATUS':
        respond({
          enabled: this.settings.enabled,
          activeDownloads: this.activeDownloads.size,
          connected: this.communicator.isConnected
        });
        break;

      case 'TOGGLE_ENABLED':
        this.settings.enabled = !this.settings.enabled;
        this.saveSettings();
        this.updateBadge();
        respond({ enabled: this.settings.enabled });
        break;

      case 'DOWNLOAD_URL':
        this.handleManualDownload(message.url, message.tabId);
        respond({ success: true });
        break;

      case 'GET_SETTINGS':
        respond({ settings: this.settings });
        break;

      case 'UPDATE_SETTINGS':
        this.settings = { ...this.settings, ...message.settings };
        this.saveSettings();
        respond({ success: true });
        break;

      case 'CHECK_CONNECTION':
        this.communicator.connect(this.settings.port).then(ok => respond({ connected: ok }));
        return true;

      case 'DOWNLOAD_COMPLETE':
        this.activeDownloads.delete(message.downloadId);
        this.updateBadge();
        break;
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
      this.showNotification('Download Started', 'Sending to MAC-1');
    } catch (e) {}
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

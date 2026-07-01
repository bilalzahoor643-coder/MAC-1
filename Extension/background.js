import { Collector } from './lib/collector.js';
import { Communicator } from './lib/communicator.js';

class BackgroundService {
  constructor() {
    this.collector = new Collector();
    this.communicator = new Communicator();

    this.activeDownloads = new Map();
    this.sentUrls = new Map();
    this.interceptedUrls = new Map();
    this.settings = {
      enabled: true,
      autoIntercept: true,
      maxConnections: 16,
      port: 57575,
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
    await this.tryConnect();
    this.updateBadge();
    setInterval(() => this.tryConnect(), 10000);
    console.log('[MAC-1] Background service initialized');
  }

  log(...args) { console.log('[MAC-1]', ...args); }

  async tryConnect() {
    try {
      const ok = await this.communicator.connect(this.settings.port);
      this.log('Connection:', ok ? 'OK' : 'FAILED');
    } catch (e) {
      this.log('Connect error:', e.message);
    }
  }

  async loadSettings() {
    try {
      const stored = await chrome.storage.local.get('settings');
      if (stored.settings) this.settings = { ...this.settings, ...stored.settings };
    } catch (e) {}
  }

  async saveSettings() {
    try { await chrome.storage.local.set({ settings: this.settings }); } catch (e) {}
  }

  setupEventListeners() {
    // DETECTION LAYER 1: onBeforeRequest (catches JS-triggered downloads like VS Code)
    chrome.webRequest.onBeforeRequest.addListener(
      (details) => this.handleBeforeRequest(details),
      { urls: ['<all_urls>'] },
      ['requestBody']
    );

    // DETECTION LAYER 2: onCreated (primary, catches all downloads)
    chrome.downloads.onCreated.addListener((item) => this.handleDownloadCreated(item));

    // DETECTION LAYER 3: onDeterminingFilename (gets correct filename from headers)
    chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
      this.handleDeterminingFilename(item, suggest);
    });

    // Metadata capture
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

    // Messages from content script / popup
    chrome.runtime.onMessage.addListener((msg, sender, respond) => {
      this.handleMessage(msg, sender, respond);
      return true;
    });

    chrome.storage.onChanged.addListener((changes) => {
      if (changes.settings) this.settings = { ...this.settings, ...changes.settings.newValue };
    });
  }

  setupContextMenus() {
    chrome.contextMenus.removeAll(() => {
      chrome.contextMenus.create({ id: 'mac1-dl-link', title: 'Download with MAC-1', contexts: ['link'] });
      chrome.contextMenus.create({ id: 'mac1-dl-page', title: 'Download all links with MAC-1', contexts: ['page'] });
    });
    chrome.contextMenus.onClicked.addListener((info, tab) => this.handleContextMenu(info, tab));
  }

  isDownloadableFileType(ext) {
    return ext && this.settings.fileTypes.includes(ext.toLowerCase());
  }

  isDownloadableUrl(url) {
    const ext = this.getFileExtension(url);
    return this.isDownloadableFileType(ext);
  }

  getFileExtension(url) {
    try {
      const path = new URL(url).pathname.split('.');
      if (path.length > 1) return path[path.length - 1].toLowerCase().split('?')[0];
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

  shouldSkipUrl(url) {
    if (!url) return true;
    if (url.startsWith('chrome-extension://')) return true;
    if (url.startsWith('chrome://')) return true;
    if (url.startsWith('about:')) return true;
    if (url.startsWith('data:')) return true;
    return false;
  }

  markSent(url, data) {
    this.sentUrls.set(url, { data, time: Date.now() });
    setTimeout(() => this.sentUrls.delete(url), 15000);
  }

  hasAlreadySent(url) {
    return this.sentUrls.has(url);
  }

  getFilenameFromSuggestion(filename) {
    if (!filename || filename === 'download' || filename === 'unknown') return null;
    return filename;
  }

  getFilenameFromHeaders(headers) {
    if (!headers) return null;
    const cd = headers['content-disposition'];
    if (!cd) return null;

    const match = cd.match(/filename\*?=(?:UTF-8''|"?)([^";\s]+)/i);
    if (match) {
      let name = match[1].replace(/"/g, '');
      try { name = decodeURIComponent(name); } catch (e) {}
      return name;
    }
    return null;
  }

  buildSession(data, filename) {
    const url = data.url || data.finalUrl || '';
    const tab = data.tab || null;
    let parsedUrl = {};
    try { parsedUrl = new URL(url); } catch (e) {}

    return {
      url: url,
      finalUrl: data.finalUrl || url,
      filename: filename || data.filename || this.extractFilename(url),
      fileExtension: this.getFileExtension(filename || url),
      fileSize: data.fileSize || 0,
      mimeType: data.mimeType || '',
      referrer: data.referrer || tab?.url || '',
      origin: data.origin || '',
      method: data.method || 'GET',
      userAgent: data.userAgent || navigator.userAgent,
      platform: data.platform || navigator.platform,
      initiator: data.initiator || '',
      host: parsedUrl.host || '',
      protocol: parsedUrl.protocol || '',
      port: parsedUrl.port ? parseInt(parsedUrl.port) : 0,
      timestamp: Date.now(),
      downloadSource: data.downloadSource || 'browser',
      resumeSupported: true,
      savePath: '',
      category: 'General',
      description: '',
      headers: data.headers || {},
      responseHeaders: data.responseHeaders || null,
      cookies: data.cookies || [],
      clientHints: data.clientHints || null,
      postData: data.postData || null,
      tab: tab,
      redirectChain: data.redirectChain || []
    };
  }

  async sendToService(session) {
    if (this.communicator.isConnected) {
      try {
        await this.communicator.sendDownload(session);
        this.log('Session sent:', session.filename);
        this.showNotification('Download Captured', `${session.filename} sent to MAC-1`);
        return true;
      } catch (e) {
        this.log('Send failed:', e.message);
      }
    }
    this.communicator.messageQueue.push(session);
    this.tryConnect();
    return false;
  }

  // ═══════════════════════════════════════════════════
  // DETECTION LAYER 1: onBeforeRequest
  // Catches JS-triggered downloads (VS Code, etc.)
  // ═══════════════════════════════════════════════════
  handleBeforeRequest(details) {
    if (details.method !== 'GET') return;
    if (this.shouldSkipUrl(details.url)) return;
    if (!this.isDownloadableUrl(details.url)) return;
    if (details.tabId < 0) return;
    if (this.hasAlreadySent(details.url)) return;

    this.interceptedUrls.set(details.url, {
      tabId: details.tabId,
      timestamp: Date.now(),
      method: details.method,
      type: details.type
    });

    setTimeout(() => this.interceptedUrls.delete(details.url), 10000);
  }

  // ═══════════════════════════════════════════════════
  // DETECTION LAYER 2: onCreated (primary)
  // ═══════════════════════════════════════════════════
  async handleDownloadCreated(downloadItem) {
    if (!this.settings.enabled || !this.settings.autoIntercept) return;
    if (this.shouldSkipUrl(downloadItem.url)) return;

    const ext = this.getFileExtension(downloadItem.url);
    if (!this.isDownloadableFileType(ext)) return;

    this.log('onCreated:', downloadItem.url);

    if (this.hasAlreadySent(downloadItem.url)) {
      this.log('Already sent, cancelling');
      try { await chrome.downloads.cancel(downloadItem.id); } catch (e) {}
      return;
    }

    this.markSent(downloadItem.url);

    this.collector.collectAllData(downloadItem).then(async (completeData) => {
      this.activeDownloads.set(downloadItem.id, completeData);
      this.updateBadge();

      try {
        await chrome.downloads.cancel(downloadItem.id);
        this.log('Cancelled download:', downloadItem.id);
      } catch (e) {}

      const filename = this.getFilenameFromSuggestion(downloadItem.filename)
        || this.getFilenameFromHeaders(completeData.responseHeaders)
        || this.extractFilename(completeData.url);

      const session = this.buildSession(completeData, filename);
      this.sendToService(session);
    }).catch(async (e) => {
      this.log('collectAllData failed:', e.message);
      try { await chrome.downloads.cancel(downloadItem.id); } catch (err) {}
    });
  }

  // ═══════════════════════════════════════════════════
  // DETECTION LAYER 3: onDeterminingFilename
  // Gets correct filename from server headers
  // ═══════════════════════════════════════════════════
  handleDeterminingFilename(item, suggest) {
    if (this.shouldSkipUrl(item.url)) {
      suggest({ filename: item.filename });
      return;
    }

    const ext = this.getFileExtension(item.url);
    if (!this.isDownloadableFileType(ext)) {
      suggest({ filename: item.filename });
      return;
    }

    let suggestedFilename = item.filename;
    const filenameFromHeader = this.getFilenameFromHeaders(item.responseHeaders);
    if (filenameFromHeader) {
      suggestedFilename = filenameFromHeader;
      this.log('Filename from Content-Disposition:', filenameFromHeader);
    }

    suggest({ filename: suggestedFilename, conflictAction: 'uniquify' });
  }

  async handleContextMenu(info, tab) {
    if (info.menuItemId === 'mac1-dl-link' && info.linkUrl) {
      if (this.hasAlreadySent(info.linkUrl)) return;
      this.markSent(info.linkUrl);
      const data = await this.collector.collectFromTab(tab, info.linkUrl);
      const session = this.buildSession(data, null);
      this.sendToService(session);
    }

    if (info.menuItemId === 'mac1-dl-page') {
      try {
        const results = await chrome.scripting.executeScript({
          target: { tabId: tab.id },
          func: () => Array.from(document.querySelectorAll('a[href]')).map(a => a.href)
        });
        const links = results[0]?.result || [];
        for (const link of links) {
          if (this.isDownloadableUrl(link) && !this.hasAlreadySent(link)) {
            this.markSent(link);
            const data = await this.collector.collectFromTab(tab, link);
            const session = this.buildSession(data, null);
            this.sendToService(session);
          }
        }
      } catch (e) {}
    }
  }

  async handleMessage(message, sender, respond) {
    switch (message.type) {
      case 'INTERCEPT_CLICK': {
        if (this.hasAlreadySent(message.url)) {
          respond({ success: true, skip: true });
          return;
        }
        this.markSent(message.url);

        const session = this.buildSession({
          url: message.url,
          tab: message.tab,
          downloadSource: 'click-intercept'
        }, message.filename);

        this.log('INTERCEPT_CLICK:', message.url);
        this.sendToService(session);
        respond({ success: true });
        break;
      }

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
        if (!this.hasAlreadySent(message.url)) {
          this.markSent(message.url);
          let tab = null;
          try {
            if (message.tabId) tab = await chrome.tabs.get(message.tabId);
            else {
              const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
              tab = tabs[0];
            }
          } catch (e) {}
          const data = await this.collector.collectFromTab(tab, message.url);
          const session = this.buildSession(data, null);
          this.sendToService(session);
        }
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
        respond({ connected: this.communicator.isConnected });
        break;

      case 'DOWNLOAD_COMPLETE':
        this.activeDownloads.delete(message.downloadId);
        this.updateBadge();
        break;
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

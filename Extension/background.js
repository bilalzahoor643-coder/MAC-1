import { Collector } from './lib/collector.js';
import { Communicator } from './lib/communicator.js';

class BackgroundService {
  constructor() {
    this.collector = new Collector();
    this.communicator = new Communicator();

    this.activeDownloads = new Map();
    this.sentUrls = new Map();
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
    console.log('[MAC-1] Extension initialized');
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
    chrome.downloads.onCreated.addListener((item) => {
      console.log('[MAC-1] onCreated FIRED:', item.url, 'mime:', item.mime, 'state:', item.state);
      this.handleDownloadCreated(item);
    });

    chrome.downloads.onChanged.addListener((delta) => {
      if (delta.state) {
        console.log('[MAC-1] onChanged:', delta.id, 'state:', delta.state.current || delta.state);
      }
    });

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
      console.log('[MAC-1] Message received:', msg.type, msg.url || '');
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

  getFileExtension(url) {
    try {
      const p = new URL(url).pathname.split('.');
      if (p.length > 1) return p[p.length - 1].toLowerCase().split('?')[0];
    } catch (e) {}
    return '';
  }

  isDownloadableByExtension(url) {
    const ext = this.getFileExtension(url);
    return ext && this.settings.fileTypes.includes(ext);
  }

  isDownloadableByMime(mime) {
    if (!mime) return false;
    const m = mime.toLowerCase();
    return m.includes('application/x-') ||
           m.includes('application/zip') ||
           m.includes('application/x-rar') ||
           m.includes('application/x-7z') ||
           m.includes('application/octet-stream') ||
           m.includes('application/pdf') ||
           m.includes('application/msword') ||
           m.includes('application/vnd.') ||
           m.includes('audio/') ||
           m.includes('video/') ||
           m.includes('application/x-msdownload') ||
           m.includes('application/x-apple');
  }

  isDownloadable(item) {
    if (this.isDownloadableByExtension(item.url)) return true;
    if (this.isDownloadableByMime(item.mime)) return true;
    if (item.filename && this.isDownloadableByExtension(item.filename)) return true;
    return false;
  }

  shouldSkipUrl(url) {
    if (!url) return true;
    if (url.startsWith('chrome-extension://')) return true;
    if (url.startsWith('chrome://')) return true;
    if (url.startsWith('about:')) return true;
    if (url.startsWith('data:')) return true;
    if (url.startsWith('blob:')) return true;
    return false;
  }

  extractFilename(url) {
    try {
      const parts = new URL(url).pathname.split('/');
      const name = parts[parts.length - 1];
      return name && name.includes('.') ? decodeURIComponent(name) : 'download';
    } catch (e) {}
    return 'download';
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

  markSent(url) {
    this.sentUrls.set(url, Date.now());
    setTimeout(() => this.sentUrls.delete(url), 15000);
  }

  hasAlreadySent(url) {
    return this.sentUrls.has(url);
  }

  buildSession(data, filename) {
    const url = data.url || '';
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
      userAgent: data.userAgent || '',
      platform: data.platform || '',
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
    console.log('[MAC-1] Sending session:', session.filename, session.url);
    if (this.communicator.isConnected) {
      try {
        await this.communicator.sendDownload(session);
        this.log('Session sent OK:', session.filename);
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

  async handleDownloadCreated(downloadItem) {
    if (!this.settings.enabled || !this.settings.autoIntercept) {
      this.log('Intercept disabled, skipping');
      return;
    }
    if (this.shouldSkipUrl(downloadItem.url)) {
      this.log('Skipping URL:', downloadItem.url);
      return;
    }

    if (!this.isDownloadable(downloadItem)) {
      this.log('Not downloadable - url:', downloadItem.url, 'ext:', this.getFileExtension(downloadItem.url), 'mime:', downloadItem.mime);
      return;
    }

    this.log('DOWNLOAD DETECTED:', downloadItem.url);

    if (this.hasAlreadySent(downloadItem.url)) {
      this.log('Already sent, cancelling');
      try { await chrome.downloads.cancel(downloadItem.id); } catch (e) {}
      return;
    }

    this.markSent(downloadItem.url);

    try {
      await chrome.downloads.cancel(downloadItem.id);
      this.log('Cancelled download:', downloadItem.id);
    } catch (e) {
      this.log('Cancel failed:', e.message);
    }

    try {
      const completeData = await this.collector.collectAllData(downloadItem);
      this.activeDownloads.set(downloadItem.id, completeData);
      this.updateBadge();

      const filename = this.getFilenameFromHeaders(completeData.responseHeaders)
        || (downloadItem.filename && downloadItem.filename !== 'download' ? downloadItem.filename : null)
        || this.extractFilename(completeData.url);

      const session = this.buildSession(completeData, filename);
      this.sendToService(session);
    } catch (e) {
      this.log('collectAllData error:', e.message);

      const session = this.buildSession({
        url: downloadItem.url,
        fileSize: downloadItem.fileSize,
        mimeType: downloadItem.mime,
        tabId: downloadItem.tabId,
        method: downloadItem.method,
        referrer: downloadItem.referrer
      }, downloadItem.filename || null);
      this.sendToService(session);
    }
  }

  async handleContextMenu(info, tab) {
    if (info.menuItemId === 'mac1-dl-link' && info.linkUrl) {
      if (this.hasAlreadySent(info.linkUrl)) return;
      this.markSent(info.linkUrl);
      const data = await this.collector.collectFromTab(tab, info.linkUrl);
      const session = this.buildSession(data, null);
      this.sendToService(session);
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

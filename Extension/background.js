import { Collector } from './lib/collector.js';
import { Communicator } from './lib/communicator.js';
import { DebugLogger } from './debug-logger.js';

class BackgroundService {
  constructor() {
    this.collector = new Collector();
    this.communicator = new Communicator();
    this.debug = new DebugLogger();
    this.activeDownloads = new Map();
    this.pendingDownloads = new Map();
    this.sentUrls = new Map();
    this.pendingRedirects = new Map(); // URL → final redirect URL
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
    this.debug.log('INIT', 'Extension initialized');
    console.log('[MAC-1] Extension initialized — Phase 2.5 DEBUG MODE');
  }

  log(...a) { console.log('[MAC-1]', ...a); }

  async tryConnect() {
    try {
      const ok = await this.communicator.connect(this.settings.port);
      this.debug.log('CONNECTION', ok ? 'Connected' : 'Failed');
    } catch (e) { this.debug.log('CONNECTION', 'Error:', e.message); }
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
    // ===== PHASE 2.5: COMPREHENSIVE DOWNLOAD TRACKING =====
    chrome.downloads.onCreated.addListener((item) => this.handleDownloadCreated(item));
    chrome.downloads.onChanged.addListener((delta) => this.handleDownloadChanged(delta));

    // ===== PHASE 2.5: LOG ALL WEB REQUESTS =====
    // Capture ALL request headers (not just download URLs)
    chrome.webRequest.onBeforeSendHeaders.addListener(
      (d) => {
        this.debug.logRequest(d);
        this.collector.captureRequestHeaders(d);
      },
      { urls: ['<all_urls>'] },
      ['requestHeaders', 'extraHeaders']
    );

    // Capture ALL response headers
    chrome.webRequest.onHeadersReceived.addListener(
      (d) => {
        this.debug.logResponse(d);
        this.collector.captureResponseHeaders(d);
      },
      { urls: ['<all_urls>'] },
      ['responseHeaders', 'extraHeaders']
    );

    // Capture ALL request bodies (POST data)
    chrome.webRequest.onBeforeRequest.addListener(
      (d) => {
        // Log form submissions and POST requests
        if (d.method === 'POST' || d.requestBody) {
          const bodyStr = d.requestBody ? JSON.stringify(d.requestBody).substring(0, 200) : 'no-body';
          this.debug.log('POST_REQUEST', d.url, d.method, bodyStr);
        }
        this.collector.capturePostData(d);
      },
      { urls: ['<all_urls>'] },
      ['requestBody']
    );

    // ===== PHASE 2.5: CAPTURE REDIRECTS =====
    chrome.webRequest.onBeforeRedirect.addListener(
      (details) => {
        this.debug.log('REDIRECT', `${details.statusCode} ${details.method} ${details.url} → ${details.redirectUrl}`);
        // Store redirect chain — when onCreated fires with original URL, we can resolve to final URL
        if (details.redirectUrl && details.statusCode >= 300 && details.statusCode < 400) {
          this.pendingRedirects.set(details.url, details.redirectUrl);
          this.debug.log('REDIRECT_STORED', details.url, '→', details.redirectUrl);
          // Auto-cleanup after 30s
          setTimeout(() => this.pendingRedirects.delete(details.url), 30000);
        }
      },
      { urls: ['<all_urls>'] }
    );

    // ===== PHASE 2.5: CAPTURE REQUEST ERRORS =====
    chrome.webRequest.onErrorOccurred.addListener(
      (details) => {
        this.debug.log('REQUEST_ERROR', `${details.method} ${details.url} — ${details.error}`);
      },
      { urls: ['<all_urls>'] }
    );

    chrome.runtime.onMessage.addListener((msg, sender, respond) => {
      this.handleMessage(msg, sender, respond);
      return true;
    });

    chrome.storage.onChanged.addListener((changes) => {
      if (changes.settings) this.settings = { ...this.settings, ...changes.settings.newValue };
    });
  }

  // ===== PHASE 2.5: COMPREHENSIVE DOWNLOAD CHANGE TRACKING =====
  async handleDownloadChanged(delta) {
    const { id, state, bytesReceived, totalBytes, filename, url, finalUrl, error, damage } = delta;

    const pending = this.pendingDownloads?.get(id);

    // Log EVERY download change event — even if not pending
    this.debug.logDownloadEvent('onChanged', {
      id,
      state: state?.current,
      bytesReceived: bytesReceived?.current,
      totalBytes: totalBytes?.current,
      filename: filename?.current,
      url: url?.current,
      finalUrl: finalUrl?.current,
      error: error?.current,
      damage: damage?.current,
      isPending: !!pending,
      pendingUrl: pending?.url
    });

    // ===== CRITICAL: Log finalUrl if different from original URL =====
    if (finalUrl?.current && url?.current && finalUrl.current !== url.current) {
      this.debug.log('URL_REDIRECT', `Download ${id}: original=${url.current} final=${finalUrl.current}`);
    }

    if (!pending || pending.cancelled) return;

    this.debug.log('onChanged:', id, '| state:', state?.current, '| bytes:', bytesReceived?.current, '| url:', pending.url);

    // Chrome completed the download natively — let it be
    if (state?.current === 'complete') {
      this.debug.log('DOWNLOAD_COMPLETE_NATIVE', pending.url);
      pending.cancelled = true;
      this.pendingDownloads.delete(id);
      this.activeDownloads.delete(id);
      this.updateBadge();
      this.showNotification('Download Complete', filename?.current || pending.session.filename);
      return;
    }

    // Chrome download interrupted — may happen if cancel failed or was too late
    if (state?.current === 'interrupted') {
      this.debug.log('DOWNLOAD_INTERRUPTED', pending.url, 'error:', delta.error?.current);
      // If still pending, try sending to service as fallback
      pending.cancelled = true;
      this.pendingDownloads.delete(id);
      if (!this.hasAlreadySent(pending.url)) {
        this.sendToService(pending.session);
        this.scheduleSizeCheck(id, pending.url);
      }
      return;
    }

    // Chrome started receiving bytes — verify it's actually a file, not HTML
    if (state?.current === 'in_progress' && bytesReceived?.current > 0) {
      this.debug.log('IN_PROGRESS', 'Chrome receiving data:', bytesReceived?.current, 'bytes');

      // Search Chrome's download record to check actual MIME type
      try {
        const results = await chrome.downloads.search({ id });
        if (results && results.length > 0) {
          const item = results[0];
          const actualMime = (item.mime || '').toLowerCase();

          // ===== PHASE 2.5: LOG FULL CHROME DOWNLOAD DETAILS =====
          this.debug.logDownloadEvent('chromeSearch', {
            id,
            url: item.url,
            finalUrl: item.finalUrl,
            filename: item.filename,
            mime: item.mime,
            totalBytes: item.totalBytes,
            bytesReceived: item.bytesReceived,
            state: item.state,
            error: item.error,
            danger: item.danger,
            conflictingAction: item.conflictingAction,
            referrer: item.referrer,
            byExtensionId: item.byExtensionId,
            byExtensionName: item.byExtensionName
          });

          // ===== CRITICAL: Log if finalUrl differs from url =====
          if (item.finalUrl && item.finalUrl !== item.url) {
            this.debug.log('FINAL_URL_DIFFERS', `Chrome download ${id}: url=${item.url} finalUrl=${item.finalUrl}`);
          }

          this.debug.log('Chrome MIME:', actualMime, '| totalBytes:', item.totalBytes, '| finalUrl:', item.finalUrl);

          // If Chrome is downloading an HTML page, let Chrome handle it natively
          if (actualMime.includes('text/html') || actualMime.includes('text/plain')) {
            this.debug.log('HTML_DETECTED', 'Letting Chrome handle natively:', pending.url);
            pending.cancelled = true;
            this.pendingDownloads.delete(id);
            this.activeDownloads.delete(id);
            this.updateBadge();
            this.showNotification('Download Page Detected', 'Browser will handle this download.');
            return;
          }

          // It's a real file download — cancel Chrome, send to service
          this.debug.log('REAL_FILE', 'Cancelling Chrome download, sending to service:', pending.url, '| mime:', actualMime, '| finalUrl:', item.finalUrl);
          pending.cancelled = true;
          this.pendingDownloads.delete(id);

          // Update session with Chrome-resolved data
          if (item.filename && item.filename !== 'download') {
            const parts = item.filename.split(/[/\\]/);
            pending.session.filename = parts[parts.length - 1] || pending.session.filename;
          }
          if (item.totalBytes && item.totalBytes > 0) {
            pending.session.fileSize = item.totalBytes;
          }
          // ===== PHASE 2.5: Use finalUrl if different =====
          // But NEVER override a redirect-resolved CDN URL with Chrome's url
          if (item.finalUrl && item.finalUrl !== item.url && !pending.session.finalUrl?.includes('dn18.')) {
            this.debug.log('USING_FINAL_URL', item.finalUrl);
            pending.session.url = item.finalUrl;
            pending.session.finalUrl = item.finalUrl;
          }

          const cancelResult = await chrome.downloads.cancel(id).catch(e => { this.debug.log('Cancel error:', e.message); return null; });
          this.debug.log('Cancel result:', cancelResult === null ? 'FAILED' : 'OK');

          this.sendToService(pending.session);
          this.scheduleSizeCheck(id, pending.url);
        } else {
          this.debug.log('Download not found in Chrome — sending to service:', pending.url);
          pending.cancelled = true;
          this.pendingDownloads.delete(id);
          this.sendToService(pending.session);
          this.scheduleSizeCheck(id, pending.url);
        }
      } catch (e) {
        this.debug.log('Error checking download:', e.message, '— sending to service');
        pending.cancelled = true;
        this.pendingDownloads.delete(id);
        try { await chrome.downloads.cancel(id); } catch (ce) {}
        this.sendToService(pending.session);
        this.scheduleSizeCheck(id, pending.url);
      }
    }
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

  isDownloadable(item) {
    const ext = this.getFileExtension(item.url);
    if (ext && this.settings.fileTypes.includes(ext)) return true;
    if (item.mime) {
      const m = item.mime.toLowerCase();
      if (m.includes('application/x-') || m.includes('application/zip') ||
          m.includes('application/x-rar') || m.includes('application/x-7z') ||
          m.includes('application/octet-stream') || m.includes('application/pdf') ||
          m.includes('application/msword') || m.includes('application/vnd.') ||
          m.includes('audio/') || m.includes('video/') ||
          m.includes('application/x-msdownload')) return true;
    }
    return false;
  }

  shouldSkipUrl(url) {
    return !url || url.startsWith('chrome-extension://') || url.startsWith('chrome://') ||
           url.startsWith('about:') || url.startsWith('data:') || url.startsWith('blob:');
  }

  extractFilename(url) {
    try {
      const parts = new URL(url).pathname.split('/');
      const name = parts[parts.length - 1];
      return name && name.includes('.') ? decodeURIComponent(name) : 'download';
    } catch (e) {}
    return 'download';
  }

  resolveFilename(downloadItem, responseHeaders) {
    const cdFilename = this.collector.parseContentDisposition(responseHeaders?.['content-disposition']);
    if (cdFilename) return cdFilename;

    if (downloadItem.filename && downloadItem.filename !== 'download') return downloadItem.filename;

    return this.extractFilename(downloadItem.url);
  }

  markSent(url) {
    this.sentUrls.set(url, Date.now());
    setTimeout(() => this.sentUrls.delete(url), 15000);
  }

  hasAlreadySent(url) { return this.sentUrls.has(url); }

  buildSession(data, filename) {
    const url = data.url || '';
    const tab = data.tab || null;
    let host = '', protocol = '', port = 0;
    try {
      const p = new URL(url);
      host = p.host;
      protocol = p.protocol;
      port = p.port ? parseInt(p.port) : (p.protocol === 'https:' ? 443 : 80);
    } catch (e) {}

    const website = tab?.url ? (() => { try { return new URL(tab.url).host; } catch(e) { return ''; } })() : '';

    return {
      url: url,
      finalUrl: data.finalUrl || url,
      filename: filename || data.filename || this.extractFilename(url),
      suggestedFilename: data.suggestedFilename || '',
      fileExtension: this.getFileExtension(filename || url),
      fileSize: data.fileSize || 0,
      mimeType: data.mimeType || '',
      referrer: data.referrer || tab?.url || '',
      origin: data.origin || '',
      method: data.method || 'GET',
      userAgent: data.userAgent || '',
      platform: data.platform || '',
      initiator: data.initiator || '',
      host: host,
      protocol: protocol,
      port: port,
      timestamp: Date.now(),
      downloadSource: data.downloadSource || 'browser',
      resumeSupported: data.resumeSupported !== false,
      savePath: '',
      category: 'General',
      description: '',
      website: website,
      websiteTitle: tab?.title || '',
      contentDisposition: data.contentDisposition || '',
      statusCode: data.statusCode || 0,
      contentLength: data.contentLength || 0,
      acceptRanges: data.acceptRanges || 'none',
      contentEncoding: data.contentEncoding || '',
      etag: data.etag || '',
      lastModified: data.lastModified || '',
      headers: data.headers || {},
      responseHeaders: data.responseHeaders || null,
      cookies: data.cookies || [],
      clientHints: data.clientHints || null,
      postData: data.postData || null,
      tab: tab,
      redirectChain: data.redirectChain || [],
      browserRawHeaders: data.browserRawHeaders || [],
      browserResponseRawHeaders: data.browserResponseRawHeaders || [],
      browserRequestType: data.browserRequestType || ''
    };
  }

  async sendToService(session) {
    const headerCount = Object.keys(session.headers || {}).length;
    const cookieCount = (session.cookies || []).length;
    const rawHeaderCount = (session.browserRawHeaders || []).length;
    const hasUserAgent = !!session.userAgent;
    const hasPostData = !!session.postData;
    console.log(`[MAC-1] Stage 3: sendToService → filename=${session.filename}, url=${session.url}, method=${session.method}, headers=${headerCount}, cookies=${cookieCount}, browserRawHeaders=${rawHeaderCount}, userAgent=${hasUserAgent}, postData=${hasPostData}, size=${session.fileSize}`);
    this.debug.log('SEND_TO_SERVICE', session.filename, '| URL:', session.url, '| finalUrl:', session.finalUrl, '| method:', session.method, '| Size:', session.fileSize, '| MIME:', session.mimeType, '| headers:', headerCount, '| cookies:', cookieCount);
    this.pendingRedirects.delete(session.url); // Cleanup
    if (this.communicator.isConnected) {
      try {
        const result = await this.communicator.sendDownload(session);
        this.debug.log('[PASS] Stage 3: Session sent to service', session.filename);
        console.log(`[MAC-1] [PASS] Stage 3: Session sent to service`);
        this.showNotification('Download Captured', `${session.filename}`);
        return true;
      } catch (e) { this.debug.log('[FAIL] Stage 3: Send failed', e.message); console.error(`[MAC-1] [FAIL] Stage 3: Send failed → ${e.message}`); }
    } else {
      this.debug.log('[FAIL] Stage 3: Service not connected, queuing');
      console.log(`[MAC-1] [FAIL] Stage 3: Service not connected, queuing`);
    }
    this.communicator.messageQueue.push(session);
    this.tryConnect();
    return false;
  }

  // ===== PHASE 2.5: COMPREHENSIVE handleDownloadCreated =====
  async handleDownloadCreated(downloadItem) {
    if (!this.settings.enabled || !this.settings.autoIntercept) return;
    if (this.shouldSkipUrl(downloadItem.url)) return;
    if (!this.isDownloadable(downloadItem)) return;

    this.debug.log('[PASS] Stage 1: Extension detected download', downloadItem.url, '| mime:', downloadItem.mime);

    // ===== PHASE 2.5: LOG FULL DOWNLOAD ITEM DETAILS =====
    this.debug.logDownloadEvent('onCreated', {
      id: downloadItem.id,
      url: downloadItem.url,
      filename: downloadItem.filename,
      mime: downloadItem.mime,
      fileSize: downloadItem.fileSize,
      totalBytes: downloadItem.totalBytes,
      bytesReceived: downloadItem.bytesReceived,
      state: downloadItem.state,
      paused: downloadItem.paused,
      danger: downloadItem.danger,
      conflict: downloadItem.conflict,
      referrer: downloadItem.referrer,
      method: downloadItem.method,
      tabId: downloadItem.tabId,
      byExtensionId: downloadItem.byExtensionId,
      byExtensionName: downloadItem.byExtensionName,
      incident: downloadItem.incident,
      finalUrl: downloadItem.finalUrl
    });

    this.log('onCreated:', downloadItem.url, '| mime:', downloadItem.mime, '| size:', downloadItem.fileSize);

    if (this.hasAlreadySent(downloadItem.url)) {
      this.debug.log('ALREADY_SENT', downloadItem.url);
      try { await chrome.downloads.cancel(downloadItem.id); } catch (e) {}
      return;
    }

    this.markSent(downloadItem.url);

    // ===== PHASE 2.5: RESOLVE REDIRECT URL =====
    // The server may have returned a 302 redirect to a CDN URL before onCreated fired
    const resolvedFinalUrl = this.pendingRedirects.get(downloadItem.url) || downloadItem.finalUrl || downloadItem.url;
    if (resolvedFinalUrl !== downloadItem.url) {
      this.debug.log('REDIRECT_RESOLVED', downloadItem.url, '→', resolvedFinalUrl);
    }

    // If redirected to a different domain (CDN), method must be GET — the POST form data was already consumed
    let resolvedMethod = downloadItem.method || 'GET';
    try {
      const origHost = new URL(downloadItem.url).hostname;
      const finalHost = new URL(resolvedFinalUrl).hostname;
      if (origHost !== finalHost) {
        this.debug.log('METHOD_OVERRIDE', `Redirect to different domain: ${origHost} → ${finalHost}, method POST→GET`);
        resolvedMethod = 'GET';
      }
    } catch (e) {}

    // Build session FIRST — before any async work — so handleDownloadChanged can find it
    const filename = this.resolveFilename(downloadItem, null);
    const session = this.buildSession({
      url: downloadItem.url,
      finalUrl: resolvedFinalUrl,
      fileSize: downloadItem.fileSize > 0 ? downloadItem.fileSize : 0,
      mimeType: downloadItem.mime,
      tabId: downloadItem.tabId,
      method: resolvedMethod,
      referrer: downloadItem.referrer,
      suggestedFilename: downloadItem.filename
    }, filename);

    this.debug.log('[PASS] Stage 2: Session created', session.filename, '| url:', session.url, '| method:', session.method);

    // Store as pending IMMEDIATELY — before collectAllData async call
    const pendingEntry = {
      session,
      url: downloadItem.url,
      created: Date.now(),
      cancelled: false
    };
    this.pendingDownloads.set(downloadItem.id, pendingEntry);

    // ═══ IMMEDIATELY cancel Chrome download ═══
    // Chrome's download bar shows the download immediately on onCreated.
    // We cancel it right away to prevent the visual glitch.
    this.debug.log('IMMEDIATE_CANCEL', 'Cancelling Chrome download:', downloadItem.url);
    pendingEntry.cancelled = true;
    this.pendingDownloads.delete(downloadItem.id);

    // Try to cancel Chrome's native download
    try {
      const cancelResult = await chrome.downloads.cancel(downloadItem.id).catch(e => {
        this.debug.log('Cancel error (non-fatal):', e.message);
        return null;
      });
      this.debug.log('Cancel result:', cancelResult === null ? 'FAILED (non-fatal)' : 'OK');
    } catch (e) {
      this.debug.log('Cancel exception (non-fatal):', e.message);
    }

    // ═══ COLLECT ENRICHED DATA FIRST ═══
    // Headers, cookies, POST data are CRITICAL for the engine to succeed.
    // Without them, servers reject the request (403, redirect to login, etc.)
    try {
      this.debug.log('COLLECTING', 'Collecting headers/cookies/POST data...');
      console.log(`[MAC-1] Stage 2.5: Calling collectAllData...`);
      const completeData = await this.collector.collectAllData(downloadItem);
      console.log(`[MAC-1] Stage 2.5: collectAllData SUCCEEDED`);
      this.activeDownloads.set(downloadItem.id, completeData);
      this.updateBadge();

      // Build ENRICHED session with all browser data
      const enrichedFilename = this.resolveFilename(downloadItem, completeData.responseHeaders);
      const enrichedSession = this.buildSession(completeData, enrichedFilename);
      // CRITICAL: Preserve redirect-resolved URL and method
      enrichedSession.finalUrl = resolvedFinalUrl;
      enrichedSession.url = downloadItem.url;
      enrichedSession.method = resolvedMethod;

      console.log(`[MAC-1] Stage 2.5 ENRICHED SESSION: headers=${Object.keys(enrichedSession.headers || {}).length}, cookies=${(enrichedSession.cookies || []).length}, browserRawHeaders=${(enrichedSession.browserRawHeaders || []).length}, userAgent=${enrichedSession.userAgent ? 'present' : 'EMPTY'}, method=${enrichedSession.method}`);
      this.debug.log('ENRICHED', downloadItem.url,
        '| headers:', Object.keys(enrichedSession.headers || {}).length,
        '| cookies:', (enrichedSession.cookies || []).length,
        '| browserRawHeaders:', (enrichedSession.browserRawHeaders || []).length,
        '| finalUrl:', resolvedFinalUrl);

      // Send ENRICHED session to service — engine needs these headers
      this.sendToService(enrichedSession);
    } catch (e) {
      console.error(`[MAC-1] Stage 2.5: collectAllData FAILED → ${e.message}`);
      console.error(`[MAC-1] Stage 2.5: Stack → ${e.stack}`);
      this.debug.log('ENRICH_ERROR', e.message, '— sending basic session');
      // Fallback: send basic session if enrichment fails
      console.log(`[MAC-1] Stage 2.5 FALLBACK: Sending basic session (NO headers/cookies) → headers=${Object.keys(session.headers || {}).length}, cookies=${(session.cookies || []).length}`);
      this.sendToService(session);
    }

    this.scheduleSizeCheck(downloadItem.id, downloadItem.url);
  }

  scheduleSizeCheck(downloadId, url) {
    setTimeout(async () => {
      try {
        const results = await chrome.downloads.search({ id: downloadId });
        if (results && results.length > 0) {
          const item = results[0];
          if (item.totalBytes && item.totalBytes > 0) {
            this.sendSizeUpdate(url, item.totalBytes);
          }
        }
      } catch (e) { this.debug.log('Size check error:', e.message); }
    }, 1500);

    setTimeout(async () => {
      try {
        const results = await chrome.downloads.search({ id: downloadId });
        if (results && results.length > 0) {
          const item = results[0];
          if (item.totalBytes && item.totalBytes > 0) {
            this.sendSizeUpdate(url, item.totalBytes);
          }
        }
      } catch (e) { this.debug.log('Size check 2 error:', e.message); }
    }, 4000);
  }

  sendSizeUpdate(url, fileSize) {
    if (!this.communicator.isConnected) return;
    fetch(`http://localhost:${this.settings.port}/api/size-update`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url, fileSize })
    }).catch(() => {});
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
        this.debug.log('INTERCEPT_CLICK:', message.url);
        this.sendToService(session);
        respond({ success: true });
        break;
      }
      case 'GET_STATUS':
        respond({ enabled: this.settings.enabled, activeDownloads: this.activeDownloads.size, connected: this.communicator.isConnected });
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

      // ===== PHASE 2.5: PAGE INTERCEPTOR MESSAGES =====
      case 'FORM_SUBMITTED':
      case 'FORM_CLICK_SUBMITTED': {
        this.debug.log('PAGE_FORM', message.type, message.action, message.method, JSON.stringify(message.formData));
        break;
      }
      case 'FETCH_CALL': {
        this.debug.log('PAGE_FETCH', message.url, message.method);
        this.debug.logFetch(message.url, message.method, 'page-fetch');
        break;
      }
      case 'XHR_SEND': {
        this.debug.log('PAGE_XHR', message.url, message.method);
        this.debug.logFetch(message.url, message.method, 'page-xhr');
        break;
      }
      case 'BLOB_URL': {
        this.debug.log('PAGE_BLOB', message.blobUrl, `type=${message.blobType} size=${message.blobSize}`);
        this.debug.logBlobUrl(message.blobUrl, 'page-blob');
        break;
      }
      case 'IFRAME_SRC': {
        this.debug.log('PAGE_IFRAME', message.src);
        break;
      }
      case 'WINDOW_OPEN': {
        this.debug.log('PAGE_WINDOW_OPEN', message.url);
        break;
      }
      case 'PAGE_ANALYSIS': {
        this.debug.log('PAGE_ANALYSIS', `links=${message.data.links.length} forms=${message.data.forms.length} scripts=${message.data.scripts.length}`);
        // Log all forms
        for (const form of message.data.forms) {
          this.debug.log('PAGE_FORM_DETAIL', JSON.stringify(form));
        }
        // Log all download-related links
        for (const link of message.data.links) {
          if (link.download || link.href.includes('.zip') || link.href.includes('.exe') || link.onclick) {
            this.debug.log('PAGE_LINK', link.href, `download=${link.download}`, `onclick=${link.onclick?.substring(0, 100)}`);
          }
        }
        // Log scripts that might be relevant
        for (const script of message.data.scripts) {
          if (script.content && (script.content.includes('download') || script.content.includes('form') || script.content.includes('submit') || script.content.includes('rand') || script.content.includes('GotoTarget'))) {
            this.debug.log('PAGE_SCRIPT', script.src || 'inline', script.content?.substring(0, 300));
          }
        }
        break;
      }

      // ===== PHASE 2.5: DEBUG COMMANDS =====
      case 'GET_DEBUG_REPORT': {
        const report = this.debug.exportTextReport();
        respond({ report });
        break;
      }
      case 'CLEAR_DEBUG_LOG': {
        this.debug.clear();
        respond({ success: true });
        break;
      }
    }
  }

  updateBadge() {
    const count = this.activeDownloads.size;
    chrome.action.setBadgeText({ text: count > 0 ? count.toString() : '' });
    chrome.action.setBadgeBackgroundColor({ color: count > 0 ? '#0077FF' : '#888888' });
  }

  showNotification(title, message) {
    chrome.notifications.create({ type: 'basic', iconUrl: 'icons/icon128.png', title, message });
  }
}

const backgroundService = new BackgroundService();

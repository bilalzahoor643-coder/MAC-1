export class Collector {
  constructor() {
    this.tabRequestHeaders = new Map();
    this.tabResponseHeaders = new Map();
    this.postData = new Map();
    this.urlRequestHeaders = new Map();
    this.urlResponseHeaders = new Map();
    this.downloadRequestHeaders = new Map();
    this.downloadResponseHeaders = new Map();
  }

  captureRequestHeaders(details) {
    if (!details.requestHeaders) return;
    const headers = {};
    const rawHeaders = [];
    for (const header of details.requestHeaders) {
      headers[header.name.toLowerCase()] = header.value;
      rawHeaders.push({ name: header.name, value: header.value });
    }
    this.tabRequestHeaders.set(details.tabId, {
      url: details.url,
      headers: headers,
      rawHeaders: rawHeaders,
      timestamp: Date.now()
    });
    this.urlRequestHeaders.set(details.url, {
      headers: headers,
      rawHeaders: rawHeaders,
      timestamp: Date.now()
    });
    this.downloadRequestHeaders.set(details.url, {
      headers: headers,
      rawHeaders: rawHeaders,
      method: details.method,
      type: details.type,
      tabId: details.tabId,
      timestamp: Date.now()
    });
  }

  captureResponseHeaders(details) {
    if (!details.responseHeaders) return;
    const headers = {};
    const rawHeaders = [];
    for (const header of details.responseHeaders) {
      headers[header.name.toLowerCase()] = header.value;
      rawHeaders.push({ name: header.name, value: header.value });
    }
    this.tabResponseHeaders.set(details.tabId, {
      url: details.url,
      headers: headers,
      rawHeaders: rawHeaders,
      statusCode: details.statusCode,
      timestamp: Date.now()
    });
    this.urlResponseHeaders.set(details.url, {
      headers: headers,
      rawHeaders: rawHeaders,
      statusCode: details.statusCode,
      timestamp: Date.now()
    });
    this.downloadResponseHeaders.set(details.url, {
      headers: headers,
      rawHeaders: rawHeaders,
      statusCode: details.statusCode,
      timestamp: Date.now()
    });
  }

  capturePostData(details) {
    if (!details.requestBody) return;
    let postData = null;
    if (details.requestBody.formData) {
      postData = details.requestBody.formData;
    } else if (details.requestBody.raw) {
      try {
        const decoder = new TextDecoder('utf-8');
        postData = decoder.decode(details.requestBody.raw[0].bytes);
      } catch (e) {}
    }
    if (postData) {
      this.postData.set(details.tabId, {
        url: details.url,
        data: postData,
        timestamp: Date.now()
      });
      // Also store by URL for cases where tabId lookup fails
      this.postData.set('url:' + details.url, {
        url: details.url,
        data: postData,
        timestamp: Date.now()
      });
    }
  }

  parseContentDisposition(cd) {
    if (!cd) return null;
    const match = cd.match(/filename\*?=(?:UTF-8''|"?)([^";\s]+)/i);
    if (match) {
      let name = match[1].replace(/"/g, '');
      try { name = decodeURIComponent(name); } catch (e) {}
      return name;
    }
    return null;
  }

  getContentLength(headers) {
    if (!headers) return 0;
    const cl = headers['content-length'];
    if (cl) {
      const size = parseInt(cl, 10);
      return isNaN(size) ? 0 : size;
    }
    return 0;
  }

  getAcceptRanges(headers) {
    if (!headers) return 'none';
    return headers['accept-ranges'] || 'none';
  }

  getETag(headers) {
    if (!headers) return '';
    return headers['etag'] || '';
  }

  getLastModified(headers) {
    if (!headers) return '';
    return headers['last-modified'] || '';
  }

  async collectAllData(downloadItem) {
    const tabId = downloadItem.tabId;
    const url = downloadItem.url;

    console.log(`[MAC-1] collectAllData: tabId=${tabId}, url=${url}`);
    console.log(`[MAC-1] Map sizes BEFORE collectAllData: tabReqHeaders=${this.tabRequestHeaders.size}, urlReqHeaders=${this.urlRequestHeaders.size}, tabRespHeaders=${this.tabResponseHeaders.size}, urlRespHeaders=${this.urlResponseHeaders.size}, postData=${this.postData.size}, downloadReqHeaders=${this.downloadRequestHeaders.size}, downloadRespHeaders=${this.downloadResponseHeaders.size}`);

    // ALL data is already captured synchronously by chrome.webRequest listeners
    // by the time onCreated fires. Read directly from Maps — no polling needed.
    const headers = await this.getRequestHeaders(tabId, url);
    const cookies = await this.getCookies(url);
    const tab = await this.getTabInfo(tabId);

    // DIAGNOSTIC: Test each sync method individually to prove which ones fail
    let response = null;
    let postDataResult = null;
    let clientHintsResult = null;
    let userAgentResult = '';
    let platformResult = '';

    try {
      response = this.getResponseHeadersSync(tabId, url);
      console.log(`[MAC-1] getResponseHeadersSync: OK → ${response ? Object.keys(response).length + ' headers' : 'null'}`);
    } catch (e) {
      console.error(`[MAC-1] getResponseHeadersSync: CRASHED → ${e.message}`);
    }

    try {
      postDataResult = this.getPostDataSync(tabId, url);
      console.log(`[MAC-1] getPostDataSync: OK → ${postDataResult ? 'available' : 'null'}`);
    } catch (e) {
      console.error(`[MAC-1] getPostDataSync: CRASHED → ${e.message}`);
    }

    try {
      clientHintsResult = this.getClientHintsSync(tabId);
      console.log(`[MAC-1] getClientHintsSync: OK → ${clientHintsResult ? Object.keys(clientHintsResult).length + ' hints' : 'null'}`);
    } catch (e) {
      console.error(`[MAC-1] getClientHintsSync: CRASHED → ${e.message}`);
    }

    try {
      userAgentResult = this.getUserAgentSync();
      console.log(`[MAC-1] getUserAgentSync: OK → ${userAgentResult ? userAgentResult.substring(0, 50) + '...' : 'empty'}`);
    } catch (e) {
      console.error(`[MAC-1] getUserAgentSync: CRASHED → ${e.message}`);
    }

    try {
      platformResult = this.getPlatformSync();
      console.log(`[MAC-1] getPlatformSync: OK → ${platformResult || 'empty'}`);
    } catch (e) {
      console.error(`[MAC-1] getPlatformSync: CRASHED → ${e.message}`);
    }

    const referrer = this.extractReferrer(tab, headers, downloadItem.referrer);
    const parsedUrl = this.parseUrl(url);
    const filename = downloadItem.filename || this.extractFilenameFromUrl(url);
    const fileExtension = this.getFileExtension(url);
    const contentLength = this.getContentLength(response);
    const fileSize = contentLength > 0 ? contentLength : (downloadItem.fileSize > 0 ? downloadItem.fileSize : 0);

    const downloadReqInfo = this.downloadRequestHeaders.get(url) || {};
    const downloadRespInfo = this.downloadResponseHeaders.get(url) || {};

    return {
      url: url,
      finalUrl: url,
      filename: filename,
      fileExtension: fileExtension,
      fileSize: fileSize,
      contentLength: contentLength,
      mimeType: downloadItem.mime || '',
      referrer: referrer,
      origin: parsedUrl.origin,
      method: (downloadReqInfo.method || downloadItem.method || 'GET').toUpperCase(),
      userAgent: userAgentResult,
      platform: platformResult,
      initiator: '',
      host: parsedUrl.host,
      protocol: parsedUrl.protocol,
      port: parsedUrl.port,
      timestamp: Date.now(),
      downloadSource: 'browser',
      resumeSupported: true,
      savePath: '',
      category: 'General',
      description: '',
      headers: headers,
      responseHeaders: response,
      cookies: cookies,
      clientHints: clientHintsResult,
      postData: postDataResult,
      tab: tab,
      redirectChain: [],
      contentDisposition: this.parseContentDisposition(response?.['content-disposition']) || '',
      acceptRanges: this.getAcceptRanges(response),
      contentEncoding: response?.['content-encoding'] || '',
      etag: this.getETag(response),
      lastModified: this.getLastModified(response),
      browserRawHeaders: downloadReqInfo.rawHeaders || [],
      browserResponseRawHeaders: downloadRespInfo.rawHeaders || [],
      browserRequestType: downloadReqInfo.type || '',
      browserTabId: downloadReqInfo.tabId ?? tabId
    };
  }

  async collectFromTab(tab, url) {
    const tabId = tab?.id;
    const [
      headers, cookies, response, postData, clientHints, userAgent, platform
    ] = await Promise.all([
      this.getRequestHeaders(tabId, url),
      this.getCookies(url),
      this.getResponseHeaders(tabId, url),
      this.getPostData(tabId, url),
      this.getClientHints(tabId),
      this.getUserAgent(),
      this.getPlatform()
    ]);

    const parsedUrl = this.parseUrl(url);
    const filename = this.extractFilenameFromUrl(url);
    const fileExtension = this.getFileExtension(url);

    return {
      url: url,
      finalUrl: url,
      filename: filename,
      fileExtension: fileExtension,
      fileSize: this.getContentLength(response),
      mimeType: '',
      referrer: tab?.url || '',
      origin: parsedUrl.origin,
      method: 'GET',
      userAgent: userAgent,
      platform: platform,
      initiator: '',
      host: parsedUrl.host,
      protocol: parsedUrl.protocol,
      port: parsedUrl.port,
      timestamp: Date.now(),
      downloadSource: 'context-menu',
      resumeSupported: true,
      savePath: '',
      category: 'General',
      description: '',
      headers: headers,
      responseHeaders: response,
      cookies: cookies,
      clientHints: clientHints,
      postData: postData,
      tab: tab ? {
        id: tab.id, url: tab.url, title: tab.title, favIconUrl: tab.favIconUrl,
        windowId: tab.windowId, frameId: 0, active: tab.active, status: tab.status
      } : null,
      redirectChain: [],
      contentDisposition: this.parseContentDisposition(response?.['content-disposition']) || '',
      acceptRanges: this.getAcceptRanges(response),
      contentEncoding: response?.['content-encoding'] || '',
      etag: this.getETag(response),
      lastModified: this.getLastModified(response),
      browserRawHeaders: [],
      browserResponseRawHeaders: [],
      browserRequestType: '',
      browserTabId: tabId
    };
  }

  async getRequestHeaders(tabId, url) {
    const byUrl = this.urlRequestHeaders.get(url);
    if (byUrl?.headers) return byUrl.headers;
    const byTab = this.tabRequestHeaders.get(tabId);
    if (byTab?.headers) return byTab.headers;
    return {
      'user-agent': navigator.userAgent,
      'accept': '*/*',
      'accept-language': navigator.language || 'en-US,en;q=0.9',
      'sec-fetch-dest': 'document',
      'sec-fetch-mode': 'navigate',
      'sec-fetch-site': 'none'
    };
  }

  async getResponseHeaders(tabId, url) {
    const byUrl = this.urlResponseHeaders.get(url);
    if (byUrl?.headers) return byUrl.headers;
    const byTab = this.tabResponseHeaders.get(tabId);
    if (byTab?.headers) return byTab.headers;
    return null;
  }

  async waitForResponseHeaders(tabId, url, timeoutMs = 2000) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      const headers = await this.getResponseHeaders(tabId, url);
      if (headers) return headers;
      await new Promise(r => setTimeout(r, 100));
    }
    return null;
  }

  async getPostData(tabId, url) {
    // Try tabId first
    const stored = this.postData.get(tabId);
    if (stored?.url === url) {
      this.postData.delete(tabId);
      this.postData.delete('url:' + url);
      return stored.data;
    }
    // Fallback: try URL key
    const byUrl = this.postData.get('url:' + url);
    if (byUrl) {
      this.postData.delete('url:' + url);
      return byUrl.data;
    }
    return null;
  }

  async getCookies(url) {
    try {
      const cookies = await chrome.cookies.getAll({ url });
      return cookies.map(c => ({
        name: c.name, value: c.value, domain: c.domain, path: c.path,
        expires: typeof c.expirationDate === 'number' ? c.expirationDate : null,
        httpOnly: c.httpOnly, secure: c.secure,
        sameSite: c.sameSite, hostOnly: c.hostOnly, session: c.session
      }));
    } catch (e) {
      return [];
    }
  }

  async getTabInfo(tabId) {
    if (!tabId || tabId < 0) return null;
    try {
      const tab = await chrome.tabs.get(tabId);
      return {
        id: tab.id, url: tab.url, title: tab.title, favIconUrl: tab.favIconUrl,
        windowId: tab.windowId, frameId: 0, active: tab.active, status: tab.status
      };
    } catch (e) {
      return null;
    }
  }

  extractReferrer(tab, headers, originalReferrer) {
    if (headers?.referer) return headers.referer;
    if (headers?.referrer) return headers.referrer;
    if (tab?.url) return tab.url;
    return originalReferrer || '';
  }

  parseUrl(url) {
    try {
      const p = new URL(url);
      return {
        origin: p.origin, host: p.host, protocol: p.protocol,
        port: p.port ? parseInt(p.port) : (p.protocol === 'https:' ? 443 : 80)
      };
    } catch (e) {
      return { origin: '', host: '', protocol: '', port: 0 };
    }
  }

  async getUserAgent() { return navigator.userAgent; }
  async getPlatform() { return navigator.platform; }

  async getClientHints(tabId) {
    const headers = (this.tabRequestHeaders.get(tabId)?.headers || this.urlRequestHeaders.values().next().value?.headers) || {};
    const ch = {};
    for (const h of ['sec-ch-ua', 'sec-ch-ua-mobile', 'sec-ch-ua-platform',
      'sec-ch-ua-full-version', 'sec-ch-ua-arch', 'sec-ch-ua-bitness',
      'sec-ch-ua-form-factors', 'sec-ch-ua-full-version-list', 'sec-ch-ua-wow64']) {
      if (headers[h]) ch[h] = headers[h];
    }
    return Object.keys(ch).length > 0 ? ch : null;
  }

  // ═══════════════════════════════════════════════════════════════
  // SYNC METHODS — read directly from Maps (already populated by
  // chrome.webRequest listeners before onCreated fires)
  // ═══════════════════════════════════════════════════════════════

  getResponseHeadersSync(tabId, url) {
    const byUrl = this.urlResponseHeaders.get(url);
    if (byUrl?.headers) return byUrl.headers;
    const byTab = this.tabResponseHeaders.get(tabId);
    if (byTab?.headers) return byTab.headers;
    return null;
  }

  getPostDataSync(tabId, url) {
    const stored = this.postData.get(tabId);
    if (stored?.url === url) return stored.data;
    const byUrl = this.postData.get('url:' + url);
    if (byUrl) return byUrl.data;
    return null;
  }

  getClientHintsSync(tabId) {
    const headers = (this.tabRequestHeaders.get(tabId)?.headers || this.urlRequestHeaders.values().next().value?.headers) || {};
    const ch = {};
    for (const h of ['sec-ch-ua', 'sec-ch-ua-mobile', 'sec-ch-ua-platform',
      'sec-ch-ua-full-version', 'sec-ch-ua-arch', 'sec-ch-ua-bitness',
      'sec-ch-ua-form-factors', 'sec-ch-ua-full-version-list', 'sec-ch-ua-wow64']) {
      if (headers[h]) ch[h] = headers[h];
    }
    return Object.keys(ch).length > 0 ? ch : null;
  }

  getUserAgentSync() {
    const first = this.urlRequestHeaders.values().next().value;
    if (first?.headers?.['user-agent']) return first.headers['user-agent'];
    return '';
  }

  getPlatformSync() {
    const first = this.urlRequestHeaders.values().next().value;
    if (first?.headers?.['sec-ch-ua-platform']) return first.headers['sec-ch-ua-platform'].replace(/"/g, '');
    return '';
  }

  extractFilenameFromUrl(url) {
    try {
      const parts = new URL(url).pathname.split('/');
      const last = parts[parts.length - 1];
      if (last && last.includes('.')) return decodeURIComponent(last);
    } catch (e) {}
    return 'download';
  }

  getFileExtension(url) {
    try {
      const parts = new URL(url).pathname.split('.');
      if (parts.length > 1) return parts[parts.length - 1].toLowerCase().split('?')[0];
    } catch (e) {}
    return '';
  }
}

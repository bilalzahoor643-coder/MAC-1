export class Collector {
  constructor() {
    this.requestHeaders = new Map();
    this.responseHeaders = new Map();
    this.postData = new Map();
    this.urlResponseHeaders = new Map();
    this.urlRequestHeaders = new Map();
  }

  captureRequestHeaders(details) {
    if (!details.requestHeaders) return;
    const headers = {};
    for (const header of details.requestHeaders) {
      headers[header.name.toLowerCase()] = header.value;
    }
    this.requestHeaders.set(details.tabId, {
      url: details.url,
      headers: headers,
      timestamp: Date.now()
    });
    this.urlRequestHeaders.set(details.url, {
      headers: headers,
      timestamp: Date.now()
    });
  }

  captureResponseHeaders(details) {
    if (!details.responseHeaders) return;
    const headers = {};
    for (const header of details.responseHeaders) {
      headers[header.name.toLowerCase()] = header.value;
    }
    this.responseHeaders.set(details.tabId, {
      url: details.url,
      headers: headers,
      statusCode: details.statusCode,
      timestamp: Date.now()
    });
    this.urlResponseHeaders.set(details.url, {
      headers: headers,
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
    const ar = headers['accept-ranges'];
    return ar || 'none';
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

    const [
      headers, cookies, tab, response, postData, clientHints, userAgent, platform
    ] = await Promise.all([
      this.getRequestHeaders(tabId, url),
      this.getCookies(url),
      this.getTabInfo(tabId),
      this.waitForResponseHeaders(tabId, url, 1500),
      this.getPostData(tabId, url),
      this.getClientHints(tabId),
      this.getUserAgent(),
      this.getPlatform()
    ]);

    const referrer = this.extractReferrer(tab, headers, downloadItem.referrer);
    const parsedUrl = this.parseUrl(url);
    const filename = downloadItem.filename || this.extractFilenameFromUrl(url);
    const fileExtension = this.getFileExtension(url);
    const contentLength = this.getContentLength(response);
    const fileSize = contentLength > 0 ? contentLength : (downloadItem.fileSize > 0 ? downloadItem.fileSize : 0);

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
      method: downloadItem.method || 'GET',
      userAgent: userAgent,
      platform: platform,
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
      clientHints: clientHints,
      postData: postData,
      tab: tab,
      redirectChain: [],
      contentDisposition: this.parseContentDisposition(response?.['content-disposition']) || '',
      acceptRanges: this.getAcceptRanges(response),
      contentEncoding: response?.['content-encoding'] || '',
      etag: this.getETag(response),
      lastModified: this.getLastModified(response)
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
      lastModified: this.getLastModified(response)
    };
  }

  async getRequestHeaders(tabId, url) {
    const byTab = this.requestHeaders.get(tabId);
    if (byTab?.headers) {
      this.requestHeaders.delete(tabId);
      return byTab.headers;
    }
    const byUrl = this.urlRequestHeaders.get(url);
    if (byUrl?.headers) return byUrl.headers;
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
    const byTab = this.responseHeaders.get(tabId);
    if (byTab?.headers) {
      this.responseHeaders.delete(tabId);
      return byTab.headers;
    }
    const byUrl = this.urlResponseHeaders.get(url);
    if (byUrl?.headers) return byUrl.headers;
    return null;
  }

  async waitForResponseHeaders(tabId, url, timeoutMs = 2000) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      const headers = this.getResponseHeaders(tabId, url);
      if (headers) return headers;
      await new Promise(r => setTimeout(r, 100));
    }
    return null;
  }

  async getPostData(tabId, url) {
    const stored = this.postData.get(tabId);
    if (stored?.url === url) {
      this.postData.delete(tabId);
      return stored.data;
    }
    return null;
  }

  async getCookies(url) {
    try {
      const cookies = await chrome.cookies.getAll({ url });
      return cookies.map(c => ({
        name: c.name, value: c.value, domain: c.domain, path: c.path,
        expires: c.expirationDate, httpOnly: c.httpOnly, secure: c.secure,
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
    const headers = this.requestHeaders.get(tabId)?.headers || {};
    const ch = {};
    for (const h of ['sec-ch-ua', 'sec-ch-ua-mobile', 'sec-ch-ua-platform',
      'sec-ch-ua-full-version', 'sec-ch-ua-arch', 'sec-ch-ua-bitness',
      'sec-ch-ua-form-factors', 'sec-ch-ua-full-version-list', 'sec-ch-ua-wow64']) {
      if (headers[h]) ch[h] = headers[h];
    }
    return Object.keys(ch).length > 0 ? ch : null;
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

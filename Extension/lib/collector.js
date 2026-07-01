export class Collector {
  constructor() {
    this.requestHeaders = new Map();
    this.responseHeaders = new Map();
    this.postData = new Map();
    this.urlResponseHeaders = new Map();
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

    setTimeout(() => this.urlResponseHeaders.delete(details.url), 30000);
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

  async collectAllData(downloadItem) {
    const tabId = downloadItem.tabId;
    const url = downloadItem.url;

    const [
      headers,
      cookies,
      tab,
      response,
      postData,
      clientHints,
      userAgent,
      platform
    ] = await Promise.all([
      this.getRequestHeaders(tabId, url),
      this.getCookies(url),
      this.getTabInfo(tabId),
      this.getResponseHeaders(tabId, url),
      this.getPostData(tabId, url),
      this.getClientHints(tabId),
      this.getUserAgent(),
      this.getPlatform()
    ]);

    const referrer = this.extractReferrer(tab, headers, downloadItem.referrer);
    const parsedUrl = this.parseUrl(url);
    const filename = downloadItem.filename || this.extractFilenameFromUrl(url);
    const fileExtension = this.getFileExtension(url);

    return {
      url: url,
      finalUrl: url,
      filename: filename,
      fileExtension: fileExtension,
      fileSize: downloadItem.fileSize || 0,
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
      redirectChain: []
    };
  }

  async collectFromTab(tab, url) {
    const tabId = tab?.id;
    const [
      headers,
      cookies,
      response,
      postData,
      clientHints,
      userAgent,
      platform
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
      fileSize: 0,
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
        id: tab.id,
        url: tab.url,
        title: tab.title,
        favIconUrl: tab.favIconUrl,
        windowId: tab.windowId,
        frameId: 0,
        active: tab.active,
        status: tab.status
      } : null,
      redirectChain: []
    };
  }

  async collectFromUrl(url, tab) {
    const parsedUrl = this.parseUrl(url);
    const filename = this.extractFilenameFromUrl(url);
    const fileExtension = this.getFileExtension(url);

    let tabData = null;
    if (tab) {
      tabData = {
        id: tab.id,
        url: tab.url || '',
        title: tab.title || '',
        favIconUrl: tab.favIconUrl || '',
        windowId: tab.windowId || 0,
        frameId: 0,
        active: tab.active || false,
        status: tab.status || ''
      };
    }

    return {
      url: url,
      finalUrl: url,
      filename: filename,
      fileExtension: fileExtension,
      fileSize: 0,
      mimeType: '',
      referrer: tab?.url || '',
      origin: parsedUrl.origin,
      method: 'GET',
      userAgent: navigator.userAgent,
      platform: navigator.platform,
      initiator: '',
      host: parsedUrl.host,
      protocol: parsedUrl.protocol,
      port: parsedUrl.port,
      timestamp: Date.now(),
      downloadSource: 'redirect-intercept',
      resumeSupported: true,
      savePath: '',
      category: 'General',
      description: '',
      headers: {
        'user-agent': navigator.userAgent,
        'accept': '*/*',
        'accept-language': navigator.language || 'en-US,en;q=0.9',
        'sec-fetch-dest': 'document',
        'sec-fetch-mode': 'navigate',
        'sec-fetch-site': 'none',
        'sec-fetch-user': '?1',
        'upgrade-insecure-requests': '1'
      },
      responseHeaders: null,
      cookies: await this.getCookies(url),
      clientHints: null,
      postData: null,
      tab: tabData,
      redirectChain: []
    };
  }

  async getRequestHeaders(tabId, url) {
    const stored = this.requestHeaders.get(tabId);
    if (stored && stored.url === url) {
      this.requestHeaders.delete(tabId);
      return stored.headers;
    }
    if (stored?.headers) return stored.headers;

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
    const stored = this.responseHeaders.get(tabId);
    if (stored && stored.url === url) {
      this.responseHeaders.delete(tabId);
      return stored.headers;
    }
    if (stored?.headers) return stored.headers;

    const byUrl = this.urlResponseHeaders.get(url);
    if (byUrl) {
      this.urlResponseHeaders.delete(url);
      return byUrl.headers;
    }

    return null;
  }

  async getPostData(tabId, url) {
    const stored = this.postData.get(tabId);
    if (stored && stored.url === url) {
      this.postData.delete(tabId);
      return stored.data;
    }
    return null;
  }

  async getCookies(url) {
    try {
      const cookies = await chrome.cookies.getAll({ url: url });
      return cookies.map(cookie => ({
        name: cookie.name,
        value: cookie.value,
        domain: cookie.domain,
        path: cookie.path,
        expires: cookie.expirationDate,
        httpOnly: cookie.httpOnly,
        secure: cookie.secure,
        sameSite: cookie.sameSite,
        hostOnly: cookie.hostOnly,
        session: cookie.session
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
        id: tab.id,
        url: tab.url,
        title: tab.title,
        favIconUrl: tab.favIconUrl,
        windowId: tab.windowId,
        frameId: 0,
        active: tab.active,
        status: tab.status
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
      const parsed = new URL(url);
      return {
        origin: parsed.origin,
        host: parsed.host,
        protocol: parsed.protocol,
        port: parsed.port ? parseInt(parsed.port) : (parsed.protocol === 'https:' ? 443 : 80)
      };
    } catch (e) {
      return { origin: '', host: '', protocol: '', port: 0 };
    }
  }

  async getUserAgent() { return navigator.userAgent; }
  async getPlatform() { return navigator.platform; }

  async getClientHints(tabId) {
    const headers = this.requestHeaders.get(tabId)?.headers || {};
    const clientHints = {};
    const chHeaders = [
      'sec-ch-ua', 'sec-ch-ua-mobile', 'sec-ch-ua-platform',
      'sec-ch-ua-full-version', 'sec-ch-ua-arch', 'sec-ch-ua-bitness',
      'sec-ch-ua-form-factors', 'sec-ch-ua-full-version-list', 'sec-ch-ua-wow64'
    ];
    for (const header of chHeaders) {
      if (headers[header]) clientHints[header] = headers[header];
    }
    return Object.keys(clientHints).length > 0 ? clientHints : null;
  }

  extractFilenameFromUrl(url) {
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
}

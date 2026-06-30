export class Collector {
  constructor() {
    this.requestHeaders = new Map();
    this.responseHeaders = new Map();
    this.postData = new Map();
    this.tabInfo = new Map();
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
  }

  capturePostData(details) {
    if (!details.requestBody) return;

    let postData = null;
    if (details.requestBody.formData) {
      postData = details.requestBody.formData;
    } else if (details.requestBody.raw) {
      const decoder = new TextDecoder('utf-8');
      postData = decoder.decode(details.requestBody.raw[0].bytes);
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

    const data = {
      url: url,
      filename: downloadItem.filename || this.extractFilename(url),
      fileSize: downloadItem.fileSize || 0,
      mimeType: downloadItem.mime || '',
      method: downloadItem.method || 'GET',
      tabId: tabId,
      timestamp: Date.now(),

      headers: await this.getRequestHeaders(tabId, url),
      cookies: await this.getCookies(url),
      tab: await this.getTabInfo(tabId),
      response: await this.getResponseHeaders(tabId, url),
      postData: await this.getPostData(tabId, url),
      clientHints: await this.getClientHints(tabId),

      referrer: await this.getReferrer(tabId, url),
      userAgent: await this.getUserAgent(),
      platform: await this.getPlatform()
    };

    return data;
  }

  async collectFromTab(tab, url) {
    const tabId = tab?.id;

    const data = {
      url: url,
      filename: this.extractFilename(url),
      fileSize: 0,
      mimeType: '',
      method: 'GET',
      tabId: tabId,
      timestamp: Date.now(),

      headers: await this.getRequestHeaders(tabId, url),
      cookies: await this.getCookies(url),
      tab: tab ? {
        id: tab.id,
        url: tab.url,
        title: tab.title,
        favIconUrl: tab.favIconUrl,
        windowId: tab.windowId,
        active: tab.active,
        status: tab.status
      } : null,
      response: await this.getResponseHeaders(tabId, url),
      postData: await this.getPostData(tabId, url),
      clientHints: await this.getClientHints(tabId),

      referrer: tab?.url || '',
      userAgent: await this.getUserAgent(),
      platform: await this.getPlatform()
    };

    return data;
  }

  async getRequestHeaders(tabId, url) {
    const stored = this.requestHeaders.get(tabId);
    if (stored && stored.url === url) {
      this.requestHeaders.delete(tabId);
      return stored.headers;
    }

    return {
      'user-agent': await this.getUserAgent(),
      'accept': '*/*',
      'accept-language': 'en-US,en;q=0.9',
      'accept-encoding': 'gzip, deflate, br',
      'referer': await this.getReferrer(tabId, url),
      'sec-fetch-dest': 'document',
      'sec-fetch-mode': 'navigate',
      'sec-fetch-site': 'none',
      'sec-fetch-user': '?1',
      'upgrade-insecure-requests': '1'
    };
  }

  async getResponseHeaders(tabId, url) {
    const stored = this.responseHeaders.get(tabId);
    if (stored && stored.url === url) {
      this.responseHeaders.delete(tabId);
      return stored.headers;
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
      console.error('[MAC-1] Failed to get cookies:', e);
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
        active: tab.active,
        status: tab.status
      };
    } catch (e) {
      return null;
    }
  }

  async getReferrer(tabId, url) {
    const tabInfo = await this.getTabInfo(tabId);
    if (tabInfo && tabInfo.url) {
      try {
        const pageUrl = new URL(tabInfo.url);
        const downloadUrl = new URL(url);
        if (pageUrl.hostname === downloadUrl.hostname) {
          return tabInfo.url;
        }
      } catch (e) {}
    }

    const stored = this.requestHeaders.get(tabId);
    if (stored && stored.headers && stored.headers.referer) {
      return stored.headers.referer;
    }

    return '';
  }

  async getUserAgent() {
    return navigator.userAgent;
  }

  async getPlatform() {
    return navigator.platform;
  }

  async getClientHints(tabId) {
    const headers = this.requestHeaders.get(tabId)?.headers || {};
    const clientHints = {};

    const chHeaders = [
      'sec-ch-ua', 'sec-ch-ua-mobile', 'sec-ch-ua-platform',
      'sec-ch-ua-full-version', 'sec-ch-ua-arch', 'sec-ch-ua-bitness',
      'sec-ch-ua-form-factors', 'sec-ch-ua-full-version-list',
      'sec-ch-ua-wow64'
    ];

    for (const header of chHeaders) {
      if (headers[header]) {
        clientHints[header] = headers[header];
      }
    }

    return Object.keys(clientHints).length > 0 ? clientHints : null;
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
}

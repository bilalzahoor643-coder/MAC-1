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
      const path = new URL(url).pathname.split('.');
      if (path.length > 1) return path[path.length - 1].toLowerCase().split('?')[0];
    } catch (e) {}
    return '';
  }

  function isDownloadable(url) {
    const ext = getFileExtension(url);
    return ext && DOWNLOAD_TYPES.includes(ext);
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
    } catch (e) {
      void e;
    }
  }

  document.addEventListener('click', (event) => {
    const link = event.target.closest('a[href]');
    if (!link) return;

    const href = link.href;
    if (!href || !isDownloadable(href)) return;

    const filename = link.download || extractFilename(href);

    sendToBackground({
      type: 'DOWNLOAD_URL',
      url: href,
      tabId: null,
      filename: filename,
      tab: getTabInfo()
    });
  }, true);

  function markDownloadLinks(element) {
    if (!element) return;
    try {
      if (element.tagName === 'A' && element.href && isDownloadable(element.href)) {
        element.setAttribute('data-mac1-download', 'true');
      }
      const links = element.querySelectorAll?.('a[href]');
      links?.forEach(link => {
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
            if (node.nodeType === Node.ELEMENT_NODE) {
              markDownloadLinks(node);
            }
          }
        }
      });
      observer.observe(document.body, { childList: true, subtree: true });
    } catch (e) {}
  }

  if (document.body) {
    startObserver();
  } else {
    document.addEventListener('DOMContentLoaded', startObserver);
  }

  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    try {
      switch (message.type) {
        case 'GET_PAGE_INFO':
          sendResponse({
            url: window.location.href,
            title: document.title,
            referrer: document.referrer,
            charset: document.characterSet,
            language: document.documentElement.lang,
            links: document.links.length,
            images: document.images.length
          });
          break;

        case 'GET_DOWNLOAD_LINKS':
          const links = [];
          document.querySelectorAll('a[href]').forEach(link => {
            const ext = getFileExtension(link.href);
            if (ext && DOWNLOAD_TYPES.includes(ext)) {
              links.push({
                url: link.href,
                text: link.textContent.trim(),
                filename: link.download || extractFilename(link.href),
                extension: ext
              });
            }
          });
          sendResponse({ links });
          break;
      }
    } catch (e) {
      sendResponse({ error: e.message });
    }
    return true;
  });
})();

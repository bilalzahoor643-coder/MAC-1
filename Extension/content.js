(function() {
  'use strict';

  class ContentScript {
    constructor() {
      this.downloadListeners = [];
      this.init();
    }

    init() {
      this.setupMessageListener();
      this.setupLinkInterception();
      this.setupDownloadDetection();
    }

    setupMessageListener() {
      chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        switch (message.type) {
          case 'GET_PAGE_INFO':
            sendResponse(this.getPageInfo());
            break;

          case 'GET_DOWNLOAD_LINKS':
            sendResponse(this.getDownloadLinks());
            break;

          case 'HIGHLIGHT_LINKS':
            this.highlightDownloadLinks(message.color);
            sendResponse({ success: true });
            break;
        }
        return true;
      });
    }

    setupLinkInterception() {
      document.addEventListener('click', (event) => {
        const link = event.target.closest('a[href]');
        if (!link) return;

        const href = link.href;
        if (!href) return;

        const extension = this.getFileExtension(href);
        if (this.isDownloadableFileType(extension)) {
          this.sendDownloadRequest(href);
        }
      }, true);
    }

    setupDownloadDetection() {
      const observer = new MutationObserver((mutations) => {
        for (const mutation of mutations) {
          for (const node of mutation.addedNodes) {
            if (node.nodeType === Node.ELEMENT_NODE) {
              this.processNewElement(node);
            }
          }
        }
      });

      observer.observe(document.body, {
        childList: true,
        subtree: true
      });
    }

    processNewElement(element) {
      if (element.tagName === 'A' && element.href) {
        const extension = this.getFileExtension(element.href);
        if (this.isDownloadableFileType(extension)) {
          this.addDownloadAttribute(element);
        }
      }

      const links = element.querySelectorAll('a[href]');
      links.forEach(link => {
        const extension = this.getFileExtension(link.href);
        if (this.isDownloadableFileType(extension)) {
          this.addDownloadAttribute(link);
        }
      });
    }

    addDownloadAttribute(link) {
      if (!link.hasAttribute('data-mac1-download')) {
        link.setAttribute('data-mac1-download', 'true');
      }
    }

    getPageInfo() {
      return {
        url: window.location.href,
        title: document.title,
        referrer: document.referrer,
        charset: document.characterSet,
        language: document.documentElement.lang,
        forms: document.forms.length,
        links: document.links.length,
        images: document.images.length
      };
    }

    getDownloadLinks() {
      const links = [];
      const allLinks = document.querySelectorAll('a[href]');

      allLinks.forEach(link => {
        const href = link.href;
        const extension = this.getFileExtension(href);

        if (this.isDownloadableFileType(extension)) {
          links.push({
            url: href,
            text: link.textContent.trim(),
            filename: this.guessFilenameFromUrl(href),
            extension: extension,
            element: {
              id: link.id,
              className: link.className,
              download: link.download
            }
          });
        }
      });

      return links;
    }

    highlightDownloadLinks(color = '#FFD700') {
      const allLinks = document.querySelectorAll('a[href]');

      allLinks.forEach(link => {
        const href = link.href;
        const extension = this.getFileExtension(href);

        if (this.isDownloadableFileType(extension)) {
          link.style.outline = `2px solid ${color}`;
          link.style.outlineOffset = '2px';
        }
      });
    }

    sendDownloadRequest(url) {
      chrome.runtime.sendMessage({
        type: 'DOWNLOAD_URL',
        url: url,
        tabId: null
      });
    }

    getFileExtension(url) {
      try {
        const urlObj = new URL(url);
        const pathParts = urlObj.pathname.split('.');
        if (pathParts.length > 1) {
          return pathParts[pathParts.length - 1].toLowerCase();
        }
      } catch (e) {}
      return '';
    }

    isDownloadableFileType(extension) {
      const downloadableTypes = [
        'exe', 'msi', 'dmg', 'apk', 'zip', 'rar', '7z', 'tar', 'gz',
        'pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx',
        'mp4', 'mkv', 'avi', 'mov', 'wmv', 'flv', 'webm',
        'mp3', 'flac', 'wav', 'aac', 'ogg',
        'iso', 'bin', 'cue', 'img'
      ];

      return downloadableTypes.includes(extension);
    }

    guessFilenameFromUrl(url) {
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

  new ContentScript();
})();

// Phase 2.5: Page-level interceptor — injected into every page
// Captures form submissions, fetch/XHR calls, and blob URL creation

(function() {
  'use strict';

  const PAGE_LOG = [];
  const sessionId = Date.now().toString(36);

  function pageLog(category, ...args) {
    const entry = {
      time: Date.now(),
      category,
      url: location.href,
      args: args.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a))
    };
    PAGE_LOG.push(entry);
    console.log(`[MAC-1-PAGE][${category}]`, ...args);
  }

  // ===== INTERCEPT FORM SUBMISSIONS =====
  const originalSubmit = HTMLFormElement.prototype.submit;
  HTMLFormElement.prototype.submit = function() {
    const form = this;
    const formData = new FormData(form);
    const data = {};
    for (const [key, value] of formData.entries()) {
      data[key] = typeof value === 'string' ? value : `[File: ${value.name}]`;
    }

    pageLog('FORM_SUBMIT', {
      action: form.action,
      method: form.method,
      id: form.id,
      name: form.name,
      enctype: form.enctype,
      target: form.target,
      data: data
    });

    // Send to background script
    try {
      chrome.runtime.sendMessage({
        type: 'FORM_SUBMITTED',
        action: form.action,
        method: form.method,
        formId: form.id,
        formData: data,
        pageUrl: location.href
      });
    } catch (e) {}

    return originalSubmit.apply(this, arguments);
  };

  // ===== INTERCEPT FORM CLICK SUBMISSIONS =====
  document.addEventListener('click', function(e) {
    const button = e.target.closest('button, input[type="submit"], input[type="button"], a[role="button"]');
    if (!button) return;

    const form = button.closest('form');
    if (!form) return;

    const formData = new FormData(form);
    const data = {};
    for (const [key, value] of formData.entries()) {
      data[key] = typeof value === 'string' ? value : `[File: ${value.name}]`;
    }

    pageLog('FORM_CLICK_SUBMIT', {
      buttonType: button.type || button.tagName,
      buttonText: (button.textContent || '').trim().substring(0, 50),
      buttonName: button.name,
      buttonValue: button.value,
      action: form.action,
      method: form.method,
      formId: form.id,
      data: data
    });

    try {
      chrome.runtime.sendMessage({
        type: 'FORM_CLICK_SUBMITTED',
        buttonType: button.type || button.tagName,
        buttonText: (button.textContent || '').trim().substring(0, 50),
        action: form.action,
        method: form.method,
        formId: form.id,
        formData: data,
        pageUrl: location.href
      });
    } catch (e) {}
  }, true);

  // ===== INTERCEPT FETCH() CALLS =====
  const originalFetch = window.fetch;
  window.fetch = function(input, init) {
    const url = typeof input === 'string' ? input : input?.url || '';
    const method = init?.method || 'GET';
    const body = init?.body || null;

    pageLog('FETCH_CALL', url, method, body ? String(body).substring(0, 200) : 'no-body');

    try {
      chrome.runtime.sendMessage({
        type: 'FETCH_CALL',
        url,
        method,
        body: body ? String(body).substring(0, 500) : null,
        pageUrl: location.href
      });
    } catch (e) {}

    return originalFetch.apply(this, arguments);
  };

  // ===== INTERCEPT XMLHttpRequest =====
  const originalXHROpen = XMLHttpRequest.prototype.open;
  const originalXHRSend = XMLHttpRequest.prototype.send;

  XMLHttpRequest.prototype.open = function(method, url, ...args) {
    this._mac1Method = method;
    this._mac1Url = url;
    return originalXHROpen.apply(this, [method, url, ...args]);
  };

  XMLHttpRequest.prototype.send = function(body) {
    pageLog('XHR_SEND', this._mac1Url, this._mac1Method, body ? String(body).substring(0, 200) : 'no-body');

    try {
      chrome.runtime.sendMessage({
        type: 'XHR_SEND',
        url: this._mac1Url,
        method: this._mac1Method,
        body: body ? String(body).substring(0, 500) : null,
        pageUrl: location.href
      });
    } catch (e) {}

    return originalXHRSend.apply(this, arguments);
  };

  // ===== INTERCEPT BLOB URL CREATION =====
  const originalCreateObjectURL = URL.createObjectURL;
  URL.createObjectURL = function(blob) {
    const url = originalCreateObjectURL.call(this, blob);
    pageLog('BLOB_URL', url, `type=${blob?.type} size=${blob?.size}`);

    try {
      chrome.runtime.sendMessage({
        type: 'BLOB_URL',
        blobUrl: url,
        blobType: blob?.type,
        blobSize: blob?.size,
        pageUrl: location.href
      });
    } catch (e) {}

    return url;
  };

  // ===== INTERCEPT WINDOW.LOCATION CHANGES =====
  let lastLocation = location.href;
  const locationObserver = new MutationObserver(() => {
    if (location.href !== lastLocation) {
      pageLog('NAVIGATION', lastLocation, '→', location.href);
      lastLocation = location.href;
    }
  });
  locationObserver.observe(document, { subtree: true, childList: true });

  // ===== INTERCEPT IFRAME CREATION =====
  const originalCreateElement = document.createElement.bind(document);
  document.createElement = function(tag, ...args) {
    const el = originalCreateElement(tag, ...args);
    if (tag.toLowerCase() === 'iframe') {
      const originalSetAttribute = el.setAttribute.bind(el);
      el.setAttribute = function(name, value) {
        if (name === 'src') {
          pageLog('IFRAME_SRC', value);
          try {
            chrome.runtime.sendMessage({
              type: 'IFRAME_SRC',
              src: value,
              pageUrl: location.href
            });
          } catch (e) {}
        }
        return originalSetAttribute(name, value);
      };

      // Also intercept direct src property set
      let iframeSrc = '';
      Object.defineProperty(el, 'src', {
        get: () => iframeSrc,
        set: (value) => {
          iframeSrc = value;
          pageLog('IFRAME_SRC_SET', value);
          try {
            chrome.runtime.sendMessage({
              type: 'IFRAME_SRC',
              src: value,
              pageUrl: location.href
            });
          } catch (e) {}
        }
      });
    }
    return el;
  };

  // ===== INTERCEPT WINDOW.OPEN =====
  const originalWindowOpen = window.open;
  window.open = function(url, ...args) {
    pageLog('WINDOW_OPEN', url, ...args);
    try {
      chrome.runtime.sendMessage({
        type: 'WINDOW_OPEN',
        url,
        pageUrl: location.href
      });
    } catch (e) {}
    return originalWindowOpen.apply(this, [url, ...args]);
  };

  // ===== INTERCEPT DOCUMENT.WRITES (potential dynamic scripts) =====
  const originalWrite = document.write;
  const originalWriteln = document.writeln;
  document.write = function(...args) {
    const content = args.join('');
    if (content.includes('download') || content.includes('form') || content.includes('submit')) {
      pageLog('DOCUMENT_WRITE', content.substring(0, 500));
    }
    return originalWrite.apply(this, args);
  };
  document.writeln = function(...args) {
    const content = args.join('');
    if (content.includes('download') || content.includes('form') || content.includes('submit')) {
      pageLog('DOCUMENT_WRITELN', content.substring(0, 500));
    }
    return originalWriteln.apply(this, args);
  };

  // ===== CAPTURE ALL LINKS ON PAGE (for analysis) =====
  function capturePageLinks() {
    const links = [];
    document.querySelectorAll('a[href]').forEach(a => {
      links.push({
        href: a.href,
        text: (a.textContent || '').trim().substring(0, 100),
        download: a.download || null,
        onclick: a.onclick ? a.onclick.toString().substring(0, 200) : null
      });
    });
    return links;
  }

  // ===== CAPTURE ALL FORMS ON PAGE (for analysis) =====
  function capturePageForms() {
    const forms = [];
    document.querySelectorAll('form').forEach(form => {
      const fields = [];
      form.querySelectorAll('input, select, textarea').forEach(field => {
        fields.push({
          type: field.type,
          name: field.name,
          value: field.value?.substring(0, 100),
          id: field.id
        });
      });
      forms.push({
        action: form.action,
        method: form.method,
        id: form.id,
        name: form.name,
        fields: fields
      });
    });
    return forms;
  }

  // ===== CAPTURE PAGE CONTENT (for analysis) =====
  function capturePageContent() {
    return {
      title: document.title,
      url: location.href,
      links: capturePageLinks(),
      forms: capturePageForms(),
      scripts: Array.from(document.querySelectorAll('script')).map(s => ({
        src: s.src || null,
        content: s.textContent?.substring(0, 500) || null
      })),
      iframes: Array.from(document.querySelectorAll('iframe')).map(f => ({
        src: f.src
      }))
    };
  }

  // ===== SEND INITIAL PAGE ANALYSIS =====
  setTimeout(() => {
    try {
      const pageData = capturePageContent();
      chrome.runtime.sendMessage({
        type: 'PAGE_ANALYSIS',
        data: pageData,
        pageUrl: location.href
      });
      pageLog('PAGE_CAPTURED', `links=${pageData.links.length} forms=${pageData.forms.length} scripts=${pageData.scripts.length}`);
    } catch (e) {}
  }, 2000);

  // ===== LISTEN FOR EXPORT REQUEST =====
  chrome.runtime.onMessage.addListener((msg, sender, respond) => {
    if (msg.type === 'GET_PAGE_LOG') {
      respond({ log: PAGE_LOG.slice(-100) });
    }
    if (msg.type === 'GET_PAGE_CONTENT') {
      respond(capturePageContent());
    }
  });

  pageLog('INIT', 'Page interceptor loaded');
})();

// Phase 2.5 Debug Logger — captures ALL network activity
// This file is imported by background.js during investigation

export class DebugLogger {
  constructor() {
    this.flowLog = [];          // Complete flow timeline
    this.requestLog = new Map(); // URL → all requests/responses
    this.downloadEvents = [];    // All chrome.downloads events
    this.blobUrls = new Set();   // Blob URLs created
    this.fetchUrls = new Set();  // Fetch/XHR URLs
    this.formSubmissions = [];   // Form submit events
    this.webNavigation = [];     // Navigation events
    this.enabled = true;
    this.sessionId = Date.now().toString(36);
  }

  log(category, ...args) {
    if (!this.enabled) return;
    const entry = {
      time: Date.now(),
      category,
      args: args.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a))
    };
    this.flowLog.push(entry);
    console.log(`[MAC-1-DEBUG][${category}]`, ...args);
  }

  // Log every webRequest with full details
  logRequest(details) {
    const { url, method, type, tabId, requestId } = details;
    const key = `${requestId || Date.now()}`;

    const entry = {
      url,
      method,
      type,
      tabId,
      requestId,
      time: Date.now(),
      requestHeaders: details.requestHeaders?.map(h => ({ name: h.name, value: (h.value || '').substring(0, 100) })) || [],
      requestBody: null
    };

    // Capture request body
    if (details.requestBody) {
      if (details.requestBody.formData) {
        entry.requestBody = { type: 'form', data: details.requestBody.formData };
      } else if (details.requestBody.raw) {
        try {
          const decoder = new TextDecoder('utf-8');
          entry.requestBody = { type: 'raw', data: decoder.decode(details.requestBody.raw[0]?.bytes) };
        } catch (e) {
          entry.requestBody = { type: 'raw', data: '(decode error)' };
        }
      }
    }

    this.requestLog.set(key, entry);
    this.flowLog.push({
      time: Date.now(),
      category: 'REQUEST',
      args: [`${method} ${type} ${url}`, JSON.stringify(entry.requestBody || 'no-body')]
    });
  }

  // Log every response
  logResponse(details) {
    const { url, statusCode, type, tabId, requestId } = details;

    const entry = {
      url,
      statusCode,
      type,
      tabId,
      requestId,
      time: Date.now(),
      responseHeaders: details.responseHeaders?.map(h => ({ name: h.name, value: (h.value || '').substring(0, 100) })) || []
    };

    this.flowLog.push({
      time: Date.now(),
      category: 'RESPONSE',
      args: [`${statusCode} ${type} ${url}`, `headers: ${entry.responseHeaders.length}`]
    });
  }

  // Log chrome.downloads events with full details
  logDownloadEvent(eventType, data) {
    const entry = {
      eventType,
      time: Date.now(),
      ...data
    };
    this.downloadEvents.push(entry);
    this.flowLog.push({
      time: Date.now(),
      category: 'DOWNLOAD_EVENT',
      args: [eventType, JSON.stringify(data).substring(0, 500)]
    });
  }

  // Log form submissions
  logFormSubmit(url, formId, formAction, formMethod, formData) {
    this.formSubmissions.push({ url, formId, formAction, formMethod, formData, time: Date.now() });
    this.flowLog.push({
      time: Date.now(),
      category: 'FORM_SUBMIT',
      args: [`formId=${formId} action=${formAction} method=${formMethod}`, JSON.stringify(formData)]
    });
  }

  // Log blob URL creation
  logBlobUrl(url, source) {
    this.blobUrls.add(url);
    this.flowLog.push({
      time: Date.now(),
      category: 'BLOB_URL',
      args: [url, source]
    });
  }

  // Log fetch/XHR
  logFetch(url, method, source) {
    this.fetchUrls.add(url);
    this.flowLog.push({
      time: Date.now(),
      category: 'FETCH',
      args: [`${method} ${url}`, source]
    });
  }

  // Export complete flow report
  exportReport() {
    const report = {
      sessionId: this.sessionId,
      exportTime: new Date().toISOString(),
      totalEvents: this.flowLog.length,
      flowTimeline: this.flowLog.slice(-200), // Last 200 events
      downloadEvents: this.downloadEvents,
      formSubmissions: this.formSubmissions,
      blobUrls: Array.from(this.blobUrls),
      fetchUrls: Array.from(this.fetchUrls),
      webNavigation: this.webNavigation,
      requestSummary: this.getRequestSummary()
    };
    return report;
  }

  getRequestSummary() {
    const summary = [];
    for (const [key, entry] of this.requestLog) {
      summary.push({
        url: entry.url,
        method: entry.method,
        type: entry.type,
        hasBody: !!entry.requestBody,
        headerCount: entry.requestHeaders.length
      });
    }
    return summary;
  }

  // Export as text for easy reading
  exportTextReport() {
    let report = `=== MAC-1 Phase 2.5 Debug Report ===\n`;
    report += `Session: ${this.sessionId}\n`;
    report += `Export Time: ${new Date().toISOString()}\n`;
    report += `Total Events: ${this.flowLog.length}\n\n`;

    report += `=== TIMELINE ===\n`;
    for (const entry of this.flowLog) {
      const time = new Date(entry.time).toISOString().substr(11, 12);
      report += `[${time}] [${entry.category}] ${entry.args.join(' | ')}\n`;
    }

    report += `\n=== DOWNLOAD EVENTS ===\n`;
    for (const event of this.downloadEvents) {
      report += JSON.stringify(event, null, 2) + '\n';
    }

    report += `\n=== FORM SUBMISSIONS ===\n`;
    for (const form of this.formSubmissions) {
      report += `URL: ${form.url}\n`;
      report += `  Form ID: ${form.formId}\n`;
      report += `  Action: ${form.formAction}\n`;
      report += `  Method: ${form.formMethod}\n`;
      report += `  Data: ${JSON.stringify(form.formData)}\n`;
    }

    report += `\n=== BLOB URLs ===\n`;
    for (const url of this.blobUrls) {
      report += `${url}\n`;
    }

    report += `\n=== FETCH/XHR URLs ===\n`;
    for (const url of this.fetchUrls) {
      report += `${url}\n`;
    }

    return report;
  }

  clear() {
    this.flowLog = [];
    this.requestLog.clear();
    this.downloadEvents = [];
    this.blobUrls.clear();
    this.fetchUrls.clear();
    this.formSubmissions = [];
    this.webNavigation = [];
  }
}

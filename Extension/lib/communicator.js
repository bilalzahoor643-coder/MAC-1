export class Communicator {
  constructor() {
    this.baseUrl = 'http://127.0.0.1:57575';
    this.isConnected = false;
    this.reconnectAttempts = 0;
    this.maxReconnectAttempts = 5;
    this.reconnectDelay = 1000;
    this.messageQueue = [];
  }

  async connect(port) {
    if (port) {
      this.baseUrl = `http://127.0.0.1:${port}`;
    }

    try {
      const response = await this.healthCheck();
      if (response && response.status === 'ok') {
        this.isConnected = true;
        this.reconnectAttempts = 0;
        console.log('[MAC-1] Connected to desktop app:', response);
        this.sendQueue();
        return true;
      }
    } catch (e) {
      console.log('[MAC-1] Connection failed, will retry...');
      this.isConnected = false;
      this.attemptReconnect();
    }
    return false;
  }

  attemptReconnect() {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) {
      console.log('[MAC-1] Max reconnect attempts reached');
      return;
    }

    this.reconnectAttempts++;
    console.log(`[MAC-1] Reconnecting... (${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

    setTimeout(() => {
      this.connect();
    }, this.reconnectDelay * this.reconnectAttempts);
  }

  async healthCheck() {
    return await this.request('/api/health', 'GET');
  }

  async ping() {
    return await this.request('/api/ping', 'GET');
  }

  async getStatus() {
    return await this.request('/api/status', 'GET');
  }

  async sendDownload(downloadData) {
    const message = {
      url: downloadData.url,
      filename: downloadData.filename,
      fileSize: downloadData.fileSize,
      referrer: downloadData.referrer,
      mimeType: downloadData.mimeType,
      savePath: downloadData.savePath,
      userAgent: downloadData.userAgent,
      cookies: downloadData.cookies,
      headers: downloadData.headers,
      tab: downloadData.tab,
      clientHints: downloadData.clientHints,
      timestamp: Date.now()
    };

    try {
      const response = await this.request('/api/download', 'POST', message);
      console.log('[MAC-1] Download sent:', response);
      return response;
    } catch (e) {
      console.error('[MAC-1] Failed to send download:', e);
      this.messageQueue.push(message);
      throw e;
    }
  }

  async request(path, method, body = null) {
    const url = `${this.baseUrl}${path}`;

    const options = {
      method: method,
      headers: {
        'Content-Type': 'application/json'
      }
    };

    if (body) {
      options.body = JSON.stringify(body);
    }

    const response = await fetch(url, options);

    if (!response.ok) {
      throw new Error(`HTTP error! status: ${response.status}`);
    }

    return await response.json();
  }

  sendQueue() {
    while (this.messageQueue.length > 0) {
      const message = this.messageQueue.shift();
      this.sendDownload(message).catch(e => {
        console.error('[MAC-1] Failed to send queued message:', e);
      });
    }
  }

  disconnect() {
    this.isConnected = false;
  }
}

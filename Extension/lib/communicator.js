export class Communicator {
  constructor() {
    this.baseUrl = 'http://127.0.0.1:57575';
    this.isConnected = false;
    this.reconnectAttempts = 0;
    this.maxReconnectAttempts = 5;
    this.reconnectDelay = 1000;
    this.messageQueue = [];
    this._healthCheckTimer = null;
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
        console.log('[MAC-1] Connected to desktop app');
        this.sendQueue();
        this.startHealthCheck();
        return true;
      }
    } catch (e) {
      this.isConnected = false;
      this.attemptReconnect();
    }
    return false;
  }

  startHealthCheck() {
    if (this._healthCheckTimer) clearInterval(this._healthCheckTimer);
    this._healthCheckTimer = setInterval(async () => {
      try {
        const response = await this.healthCheck();
        if (response && response.status === 'ok') {
          this.isConnected = true;
        } else {
          this.isConnected = false;
        }
      } catch (e) {
        this.isConnected = false;
      }
    }, 30000);
  }

  attemptReconnect() {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) return;
    this.reconnectAttempts++;
    setTimeout(() => this.connect(), this.reconnectDelay * this.reconnectAttempts);
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
    try {
      const response = await this.request('/api/session', 'POST', downloadData);
      console.log('[MAC-1] Session sent successfully');
      return response;
    } catch (e) {
      console.error('[MAC-1] Session send failed, trying fallback:', e);
      try {
        const fallbackResponse = await this.request('/api/download', 'POST', downloadData);
        return fallbackResponse;
      } catch (e2) {
        console.error('[MAC-1] Fallback also failed:', e2);
        this.messageQueue.push(downloadData);
        throw e2;
      }
    }
  }

  async request(path, method, body = null) {
    const url = `${this.baseUrl}${path}`;
    const options = {
      method: method,
      headers: { 'Content-Type': 'application/json' }
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
    if (this._healthCheckTimer) {
      clearInterval(this._healthCheckTimer);
      this._healthCheckTimer = null;
    }
  }
}

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
    if (port) this.baseUrl = `http://127.0.0.1:${port}`;
    try {
      const response = await fetch(`${this.baseUrl}/api/health`, {
        method: 'GET',
        signal: AbortSignal.timeout(3000)
      });
      if (response.ok) {
        this.isConnected = true;
        this.reconnectAttempts = 0;
        this.sendQueue();
        return true;
      }
    } catch (e) {
      this.isConnected = false;
      this.attemptReconnect();
    }
    return false;
  }

  attemptReconnect() {
    if (this.reconnectAttempts >= this.maxReconnectAttempts) return;
    this.reconnectAttempts++;
    setTimeout(() => this.connect(), this.reconnectDelay * this.reconnectAttempts);
  }

  async sendDownload(downloadData) {
    const body = JSON.stringify(downloadData);

    try {
      const response = await fetch(`${this.baseUrl}/api/session`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: body,
        signal: AbortSignal.timeout(5000)
      });
      if (response.ok) return await response.json();
    } catch (e) {}

    try {
      const response = await fetch(`${this.baseUrl}/api/download`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: body,
        signal: AbortSignal.timeout(5000)
      });
      if (response.ok) return await response.json();
    } catch (e) {
      this.messageQueue.push(downloadData);
      throw e;
    }
  }

  sendQueue() {
    while (this.messageQueue.length > 0) {
      const msg = this.messageQueue.shift();
      this.sendDownload(msg).catch(() => {});
    }
  }

  disconnect() {
    this.isConnected = false;
  }
}

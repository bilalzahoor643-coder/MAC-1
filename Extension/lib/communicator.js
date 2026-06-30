export class Communicator {
  constructor() {
    this.port = null;
    this.isConnected = false;
    this.reconnectAttempts = 0;
    this.maxReconnectAttempts = 5;
    this.reconnectDelay = 1000;
    this.messageQueue = [];
    this.pendingResponses = new Map();
    this.portName = 'MAC-1-Extension';
  }

  connect(targetPort) {
    try {
      this.port = chrome.runtime.connectNative('com.mac1.downloader');

      this.port.onMessage.addListener((message) => {
        this.handleMessage(message);
      });

      this.port.onDisconnect.addListener(() => {
        this.isConnected = false;
        console.log('[MAC-1] Disconnected from desktop app');
        this.attemptReconnect();
      });

      this.isConnected = true;
      this.reconnectAttempts = 0;
      console.log('[MAC-1] Connected to desktop app');

      this.sendQueue();

    } catch (e) {
      console.error('[MAC-1] Connection failed:', e);
      this.attemptReconnect();
    }
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

  send(message) {
    if (!this.isConnected || !this.port) {
      this.messageQueue.push(message);
      return false;
    }

    try {
      this.port.postMessage(message);
      return true;
    } catch (e) {
      console.error('[MAC-1] Failed to send message:', e);
      this.messageQueue.push(message);
      return false;
    }
  }

  sendDownload(downloadData) {
    const message = {
      type: 'DOWNLOAD_REQUEST',
      data: downloadData,
      timestamp: Date.now()
    };

    return this.send(message);
  }

  sendProgress(downloadId, progress) {
    const message = {
      type: 'DOWNLOAD_PROGRESS',
      downloadId: downloadId,
      progress: progress,
      timestamp: Date.now()
    };

    return this.send(message);
  }

  sendComplete(downloadId) {
    const message = {
      type: 'DOWNLOAD_COMPLETE',
      downloadId: downloadId,
      timestamp: Date.now()
    };

    return this.send(message);
  }

  sendError(downloadId, error) {
    const message = {
      type: 'DOWNLOAD_ERROR',
      downloadId: downloadId,
      error: error,
      timestamp: Date.now()
    };

    return this.send(message);
  }

  handleMessage(message) {
    console.log('[MAC-1] Received:', message);

    switch (message.type) {
      case 'DOWNLOAD_RESPONSE':
        this.handleDownloadResponse(message);
        break;

      case 'PROGRESS_UPDATE':
        this.handleProgressUpdate(message);
        break;

      case 'DOWNLOAD_COMPLETE':
        this.handleDownloadComplete(message);
        break;

      case 'DOWNLOAD_ERROR':
        this.handleDownloadError(message);
        break;

      case 'SETTINGS_SYNC':
        this.handleSettingsSync(message);
        break;

      default:
        console.log('[MAC-1] Unknown message type:', message.type);
    }
  }

  handleDownloadResponse(message) {
    const { downloadId, success, error } = message;
    if (success) {
      console.log(`[MAC-1] Download started: ${downloadId}`);
    } else {
      console.error(`[MAC-1] Download failed: ${error}`);
    }

    const callback = this.pendingResponses.get(downloadId);
    if (callback) {
      callback(message);
      this.pendingResponses.delete(downloadId);
    }
  }

  handleProgressUpdate(message) {
    const { downloadId, progress } = message;
    chrome.runtime.sendMessage({
      type: 'PROGRESS_UPDATE',
      downloadId: downloadId,
      progress: progress
    }).catch(() => {});
  }

  handleDownloadComplete(message) {
    const { downloadId } = message;
    chrome.runtime.sendMessage({
      type: 'DOWNLOAD_COMPLETE',
      downloadId: downloadId
    }).catch(() => {});

    chrome.downloads.search({ id: downloadId }, (items) => {
      if (items.length > 0) {
        const item = items[0];
        this.showNotification(
          'Download Complete',
          `${item.filename} has been downloaded`
        );
      }
    });
  }

  handleDownloadError(message) {
    const { downloadId, error } = message;
    chrome.runtime.sendMessage({
      type: 'DOWNLOAD_ERROR',
      downloadId: downloadId,
      error: error
    }).catch(() => {});

    this.showNotification(
      'Download Failed',
      error || 'An error occurred during download'
    );
  }

  handleSettingsSync(message) {
    const { settings } = message;
    chrome.storage.local.set({ settings: settings });
  }

  sendQueue() {
    while (this.messageQueue.length > 0) {
      const message = this.messageQueue.shift();
      this.send(message);
    }
  }

  showNotification(title, message) {
    chrome.notifications.create({
      type: 'basic',
      iconUrl: 'icons/icon128.png',
      title: title,
      message: message
    });
  }

  disconnect() {
    if (this.port) {
      this.port.disconnect();
      this.port = null;
      this.isConnected = false;
    }
  }
}

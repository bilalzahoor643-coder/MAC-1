export class Utils {
  constructor() {
    this.formatSizes = ['B', 'KB', 'MB', 'GB', 'TB'];
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

  formatFileSize(bytes) {
    if (bytes <= 0) return '0 B';

    let i = 0;
    let size = bytes;
    while (size >= 1024 && i < this.formatSizes.length - 1) {
      size /= 1024;
      i++;
    }

    return `${size.toFixed(2)} ${this.formatSizes[i]}`;
  }

  formatSpeed(bytesPerSecond) {
    return `${this.formatFileSize(bytesPerSecond)}/s`;
  }

  formatTime(seconds) {
    if (seconds <= 0 || !isFinite(seconds)) return '--:--';

    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);

    if (hours > 0) {
      return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
    return `${minutes}:${secs.toString().padStart(2, '0')}`;
  }

  sanitizeFilename(filename) {
    return filename
      .replace(/[<>:"/\\|?*]/g, '_')
      .replace(/\.{2,}/g, '.')
      .replace(/^\./, '')
      .trim();
  }

  isValidUrl(string) {
    try {
      new URL(string);
      return true;
    } catch (e) {
      return false;
    }
  }

  getDomainFromUrl(url) {
    try {
      const urlObj = new URL(url);
      return urlObj.hostname;
    } catch (e) {
      return '';
    }
  }

  sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
  }

  generateId() {
    return Date.now().toString(36) + Math.random().toString(36).substr(2);
  }

  deepClone(obj) {
    return JSON.parse(JSON.stringify(obj));
  }

  debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
      const later = () => {
        clearTimeout(timeout);
        func(...args);
      };
      clearTimeout(timeout);
      timeout = setTimeout(later, wait);
    };
  }

  throttle(func, limit) {
    let inThrottle;
    return function executedFunction(...args) {
      if (!inThrottle) {
        func(...args);
        inThrottle = true;
        setTimeout(() => inThrottle = false, limit);
      }
    };
  }
}

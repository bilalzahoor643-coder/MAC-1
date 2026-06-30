document.addEventListener('DOMContentLoaded', () => {
  const enabled = document.getElementById('enabled');
  const autoIntercept = document.getElementById('autoIntercept');
  const port = document.getElementById('port');
  const maxConnections = document.getElementById('maxConnections');
  const captureHeaders = document.getElementById('captureHeaders');
  const captureCookies = document.getElementById('captureCookies');
  const capturePostData = document.getElementById('capturePostData');
  const captureClientHints = document.getElementById('captureClientHints');
  const fileTypes = document.getElementById('fileTypes');
  const saveBtn = document.getElementById('saveBtn');
  const resetBtn = document.getElementById('resetBtn');

  const defaultSettings = {
    enabled: true,
    autoIntercept: true,
    port: 57575,
    maxConnections: 16,
    captureHeaders: true,
    captureCookies: true,
    capturePostData: true,
    captureClientHints: true,
    fileTypes: 'exe, msi, dmg, apk, zip, rar, 7z, tar, gz, pdf, doc, docx, xls, xlsx, mp4, mkv, avi, mp3, flac, iso'
  };

  init();

  async function init() {
    await loadSettings();
    setupEventListeners();
  }

  async function loadSettings() {
    try {
      const response = await chrome.runtime.sendMessage({ type: 'GET_SETTINGS' });
      if (response && response.settings) {
        applySettings(response.settings);
      }
    } catch (e) {
      console.error('Failed to load settings:', e);
    }
  }

  function applySettings(settings) {
    enabled.checked = settings.enabled !== undefined ? settings.enabled : defaultSettings.enabled;
    autoIntercept.checked = settings.autoIntercept !== undefined ? settings.autoIntercept : defaultSettings.autoIntercept;
    port.value = settings.port || defaultSettings.port;
    maxConnections.value = settings.maxConnections || defaultSettings.maxConnections;
    captureHeaders.checked = settings.captureHeaders !== undefined ? settings.captureHeaders : defaultSettings.captureHeaders;
    captureCookies.checked = settings.captureCookies !== undefined ? settings.captureCookies : defaultSettings.captureCookies;
    capturePostData.checked = settings.capturePostData !== undefined ? settings.capturePostData : defaultSettings.capturePostData;
    captureClientHints.checked = settings.captureClientHints !== undefined ? settings.captureClientHints : defaultSettings.captureClientHints;
    fileTypes.value = settings.fileTypes || defaultSettings.fileTypes;
  }

  function getSettings() {
    return {
      enabled: enabled.checked,
      autoIntercept: autoIntercept.checked,
      port: parseInt(port.value, 10),
      maxConnections: parseInt(maxConnections.value, 10),
      captureHeaders: captureHeaders.checked,
      captureCookies: captureCookies.checked,
      capturePostData: capturePostData.checked,
      captureClientHints: captureClientHints.checked,
      fileTypes: fileTypes.value
    };
  }

  function setupEventListeners() {
    saveBtn.addEventListener('click', saveSettings);
    resetBtn.addEventListener('click', resetSettings);
  }

  async function saveSettings() {
    const settings = getSettings();

    try {
      await chrome.runtime.sendMessage({
        type: 'UPDATE_SETTINGS',
        settings: settings
      });
      showNotification('Settings saved successfully!');
    } catch (e) {
      console.error('Failed to save settings:', e);
      showNotification('Failed to save settings');
    }
  }

  async function resetSettings() {
    applySettings(defaultSettings);
    await saveSettings();
    showNotification('Settings reset to default');
  }

  function showNotification(message) {
    const notification = document.createElement('div');
    notification.style.cssText = `
      position: fixed;
      bottom: 24px;
      left: 50%;
      transform: translateX(-50%);
      padding: 12px 24px;
      background: #333333;
      color: white;
      border-radius: 8px;
      font-size: 14px;
      z-index: 1000;
      animation: fadeInOut 2s ease-in-out;
    `;
    notification.textContent = message;
    document.body.appendChild(notification);

    setTimeout(() => {
      notification.remove();
    }, 2000);
  }
});

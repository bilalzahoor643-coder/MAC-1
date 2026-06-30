document.addEventListener('DOMContentLoaded', () => {
  const enabledToggle = document.getElementById('enabledToggle');
  const statusIndicator = document.getElementById('statusIndicator');
  const activeCount = document.getElementById('activeCount');
  const urlInput = document.getElementById('urlInput');
  const downloadBtn = document.getElementById('downloadBtn');
  const linksList = document.getElementById('linksList');
  const refreshLinks = document.getElementById('refreshLinks');
  const settingsBtn = document.getElementById('settingsBtn');
  const openAppBtn = document.getElementById('openAppBtn');

  init();

  async function init() {
    await loadStatus();
    await loadLinks();
    setupEventListeners();
  }

  async function loadStatus() {
    try {
      const response = await chrome.runtime.sendMessage({ type: 'GET_STATUS' });
      if (response) {
        enabledToggle.checked = response.enabled;
        activeCount.textContent = response.activeDownloads;
        updateConnectionStatus(response.connected);
      }
    } catch (e) {
      console.error('Failed to load status:', e);
    }
  }

  function updateConnectionStatus(connected) {
    if (connected) {
      statusIndicator.classList.remove('disconnected');
      statusIndicator.querySelector('.text').textContent = 'Connected';
    } else {
      statusIndicator.classList.add('disconnected');
      statusIndicator.querySelector('.text').textContent = 'Disconnected';
    }
  }

  async function loadLinks() {
    try {
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!tab) return;

      const response = await chrome.tabs.sendMessage(tab.id, { type: 'GET_DOWNLOAD_LINKS' });
      if (response && response.length > 0) {
        displayLinks(response);
      } else {
        linksList.innerHTML = '<div class="empty-state">No download links found on this page</div>';
      }
    } catch (e) {
      linksList.innerHTML = '<div class="empty-state">No download links found on this page</div>';
    }
  }

  function displayLinks(links) {
    linksList.innerHTML = '';

    links.slice(0, 10).forEach(link => {
      const linkItem = document.createElement('div');
      linkItem.className = 'link-item';
      linkItem.innerHTML = `
        <div class="link-icon">${link.extension.toUpperCase()}</div>
        <div class="link-info">
          <div class="link-name">${link.filename}</div>
          <div class="link-meta">${link.text || link.url}</div>
        </div>
        <button class="link-download-btn" data-url="${link.url}">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
            <polyline points="7 10 12 15 17 10"/>
            <line x1="12" y1="15" x2="12" y2="3"/>
          </svg>
        </button>
      `;

      linkItem.querySelector('.link-download-btn').addEventListener('click', (e) => {
        e.stopPropagation();
        downloadFile(link.url);
      });

      linkItem.addEventListener('click', () => {
        downloadFile(link.url);
      });

      linksList.appendChild(linkItem);
    });
  }

  function setupEventListeners() {
    enabledToggle.addEventListener('change', toggleEnabled);
    downloadBtn.addEventListener('click', handleManualDownload);
    urlInput.addEventListener('keypress', (e) => {
      if (e.key === 'Enter') handleManualDownload();
    });
    refreshLinks.addEventListener('click', loadLinks);
    settingsBtn.addEventListener('click', openSettings);
    openAppBtn.addEventListener('click', openApp);
  }

  async function toggleEnabled() {
    try {
      const response = await chrome.runtime.sendMessage({ type: 'TOGGLE_ENABLED' });
      if (response) {
        enabledToggle.checked = response.enabled;
      }
    } catch (e) {
      console.error('Failed to toggle:', e);
    }
  }

  async function handleManualDownload() {
    const url = urlInput.value.trim();
    if (!url) return;

    try {
      await chrome.runtime.sendMessage({
        type: 'DOWNLOAD_URL',
        url: url
      });
      urlInput.value = '';
      urlInput.placeholder = 'Download started!';
      setTimeout(() => {
        urlInput.placeholder = 'Paste download link here...';
      }, 2000);
    } catch (e) {
      console.error('Failed to start download:', e);
    }
  }

  async function downloadFile(url) {
    try {
      await chrome.runtime.sendMessage({
        type: 'DOWNLOAD_URL',
        url: url
      });
    } catch (e) {
      console.error('Failed to download:', e);
    }
  }

  function openSettings() {
    chrome.runtime.openOptionsPage();
  }

  function openApp() {
    chrome.tabs.create({ url: 'http://127.0.0.1:57575' });
  }
});

(() => {
  const params = new URLSearchParams(window.location.search);
  const redirectUrl = params.get('redirect');
  const statusEl = document.getElementById('status');

  if (redirectUrl) {
    statusEl.textContent = 'Sending to MAC-1...';

    chrome.runtime.sendMessage({
      type: 'REDIRECT_DOWNLOAD',
      url: redirectUrl
    }, (response) => {
      if (response && response.success) {
        statusEl.textContent = 'Download captured! Closing...';
      } else {
        statusEl.textContent = 'Failed. Opening in browser...';
        window.location.href = redirectUrl;
      }
      setTimeout(() => window.close(), 500);
    });
  } else {
    statusEl.textContent = 'No URL found. Opening in browser...';
    setTimeout(() => window.close(), 1000);
  }
})();

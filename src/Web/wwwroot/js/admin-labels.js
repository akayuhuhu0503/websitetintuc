"use strict";
(() => {
  const source = document.getElementById('admin-display-labels');
  const labels = source ? JSON.parse(source.textContent) : {};
  window.landPortalStateLabel = code => labels[code] || 'Cần kiểm tra trạng thái';
})();

"use strict";
(() => {
  const dates = Array.from(document.querySelectorAll('[data-vietnamese-date]'));
  function parsed(value) {
    const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(value);
    if (!match) return null;
    const [day, month, year] = match.slice(1).map(Number);
    const date = new Date(0); date.setUTCFullYear(year, month - 1, day); date.setUTCHours(0, 0, 0, 0);
    return year >= 1 && date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day ? year * 10000 + month * 100 + day : null;
  }
  function validateDates() {
    dates.forEach(input => input.setCustomValidity(input.value && parsed(input.value) === null ? 'Ngày không hợp lệ. Nhập dd/MM/yyyy, ví dụ 23/10/2026.' : ''));
    [['issued-from', 'issued-to'], ['filter-from', 'filter-to']].forEach(([a, b]) => {
      const start = document.getElementById(a), end = document.getElementById(b);
      if (start && end && parsed(start.value) !== null && parsed(end.value) !== null && parsed(start.value) > parsed(end.value)) end.setCustomValidity('Đến ngày phải bằng hoặc sau Từ ngày. Nhập theo dd/MM/yyyy.');
    });
  }
  dates.forEach(input => input.addEventListener('input', validateDates));
  validateDates();
  document.querySelectorAll('[data-private-file]').forEach(input => input.addEventListener('change', () => {
    const label = input.parentElement.querySelector('[data-private-file-name]');
    if (label) label.textContent = input.files?.[0]?.name || 'Chưa chọn tệp';
  }));
  document.querySelectorAll('[data-public-submit]').forEach(form => form.addEventListener('submit', event => {
    if (form.getAttribute('aria-busy') === 'true') { event.preventDefault(); return; }
    form.setAttribute('aria-busy', 'true');
    const button = event.submitter;
    if (button) {
      button.setAttribute('aria-disabled', 'true');
      button.dataset.originalLabel = button.textContent;
      button.textContent = button.hasAttribute('formnovalidate') ? 'Đang làm mới câu hỏi…' : 'Đang gửi nội dung…';
    }
  }));
  window.addEventListener('pageshow', () => {
    document.querySelectorAll('[data-public-submit]').forEach(form => { form.removeAttribute('aria-busy'); form.querySelectorAll('[data-original-label]').forEach(button => { button.textContent = button.dataset.originalLabel; button.removeAttribute('aria-disabled'); }); });
  });
})();

(() => {
    const editor = document.querySelector('[data-role-editor]');
    if (!editor) return;
    editor.querySelectorAll('[data-role-preset]').forEach(button => button.addEventListener('click', () => {
        const keys = button.dataset.rolePreset.split(' ');
        const choices = [...editor.querySelectorAll('input[type="checkbox"][name="Permissions"]')];
        if (keys.some(key => !choices.some(input => input.value === key && !input.disabled))) return;
        choices.forEach(input => { if (!input.disabled && keys.includes(input.value)) input.checked = true; });
        editor.querySelector('[data-role-selection-status]').textContent = 'Đã chọn thêm quyền gợi ý trên biểu mẫu. Các quyền cũ vẫn giữ nguyên. Chưa lưu hoặc cấp quyền cho ai.';
    }));
})();

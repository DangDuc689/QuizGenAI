// JS tương tác cho dự án QuizGen AI (Sidebar & Header & Chế độ Sáng/Tối)

$(document).ready(function () {
    // -------------------------------------------------------------
    // 1. QUẢN LÝ SIDEBAR DI ĐỘNG (MOBILE SIDEBAR TOGGLE)
    // -------------------------------------------------------------
    const $sidebar = $('#sidebar');
    const $sidebarOverlay = $('#sidebar-overlay');
    const $sidebarToggle = $('#sidebar-toggle');

    // Hàm mở Sidebar di động
    function openSidebar() {
        $sidebarOverlay.removeClass('hidden');
        // Cho phép trình duyệt render xong thuộc tính hidden trước khi thêm hiệu ứng opacity
        setTimeout(() => {
            $sidebarOverlay.removeClass('opacity-0').addClass('opacity-100 pointer-events-auto');
            $sidebar.removeClass('-translate-x-full').addClass('translate-x-0');
        }, 10);
    }

    // Hàm đóng Sidebar di động
    function closeSidebar() {
        $sidebar.removeClass('translate-x-0').addClass('-translate-x-full');
        $sidebarOverlay.removeClass('opacity-100 pointer-events-auto').addClass('opacity-0');
        
        // Đợi hiệu ứng transition trượt (300ms) hoàn tất mới ẩn hẳn phần tử overlay
        setTimeout(() => {
            $sidebarOverlay.addClass('hidden');
        }, 300);
    }

    // Sự kiện mở Sidebar
    $sidebarToggle.on('click', function (e) {
        e.stopPropagation();
        openSidebar();
    });

    // Click ra ngoài (lớp phủ) -> tự động đóng Sidebar
    $sidebarOverlay.on('click', function () {
        closeSidebar();
    });

    // Click vào chính Sidebar -> không đóng
    $sidebar.on('click', function (e) {
        e.stopPropagation();
    });

    // -------------------------------------------------------------
    // 2. QUẢN LÝ CHẾ ĐỘ SÁNG/TỐI (LIGHT/DARK MODE SWITCHER)
    // -------------------------------------------------------------
    const $darkModeToggle = $('#dark-mode-toggle');
    const $darkModeIcon = $('#dark-mode-icon');

    // Hàm áp dụng chế độ tối (Dark Theme)
    function applyDarkTheme() {
        $('html').addClass('dark');
        $darkModeIcon.text('light_mode');
        localStorage.theme = 'dark';
    }

    // Hàm áp dụng chế độ sáng (Light Theme)
    function applyLightTheme() {
        $('html').removeClass('dark');
        $darkModeIcon.text('dark_mode');
        localStorage.theme = 'light';
    }

    // Khởi tạo trạng thái ban đầu khi tải trang (Đồng bộ biểu tượng dựa trên class dark đang có)
    function initializeTheme() {
        if ($('html').hasClass('dark')) {
            $darkModeIcon.text('light_mode');
        } else {
            $darkModeIcon.text('dark_mode');
        }
    }

    // Bắt sự kiện Click chuyển đổi theme
    $darkModeToggle.on('click', function () {
        if ($('html').hasClass('dark')) {
            applyLightTheme();
        } else {
            applyDarkTheme();
        }
        // Phát sự kiện để đồng bộ biểu đồ Chart.js và các thành phần khác
        window.dispatchEvent(new Event('theme-change'));
    });

    // Chạy cấu hình ban đầu
    initializeTheme();
});

// -------------------------------------------------------------
// 3. HÀM XÁC NHẬN TÙY CHỈNH (CUSTOM CONFIRM MODAL API)
// -------------------------------------------------------------
window.showConfirmModal = function (options) {
    const { title, message, onConfirm } = options;
    const $modal = $('#custom-confirm-modal');
    const $overlay = $('#confirm-modal-overlay');
    const $box = $('#confirm-modal-box');
    const $title = $('#confirm-modal-title');
    const $message = $('#confirm-modal-message');
    const $btnCancel = $('#confirm-modal-cancel');
    const $btnSubmit = $('#confirm-modal-submit');

    $title.text(title || "Xác nhận");
    $message.html(message || "Bạn có chắc chắn muốn thực hiện hành động này?");

    // Hiển thị modal và backdrop với hiệu ứng transition
    $modal.removeClass('hidden').addClass('flex');
    setTimeout(() => {
        $overlay.removeClass('opacity-0').addClass('opacity-100');
        $box.removeClass('scale-95 opacity-0').addClass('scale-100 opacity-100');
    }, 10);

    // Hàm đóng modal
    function hideModal() {
        $overlay.removeClass('opacity-100').addClass('opacity-0');
        $box.removeClass('scale-100 opacity-100').addClass('scale-95 opacity-0');
        setTimeout(() => {
            $modal.removeClass('flex').addClass('hidden');
            // Gỡ bỏ sự kiện click để tránh rò rỉ bộ nhớ hoặc gọi trùng lặp
            $btnSubmit.off('click');
            $btnCancel.off('click');
            $overlay.off('click');
        }, 300);
    }

    // Gắn sự kiện cho các nút đóng
    $btnCancel.on('click', hideModal);
    $overlay.on('click', hideModal);

    // Sự kiện khi bấm đồng ý
    $btnSubmit.on('click', function () {
        if (typeof onConfirm === 'function') {
            onConfirm();
        }
        hideModal();
    });
};

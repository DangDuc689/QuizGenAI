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

    // Khởi tạo trạng thái ban đầu khi tải trang (Mặc định là Light Mode để đảm bảo giao diện sáng sủa)
    function initializeTheme() {
        if (localStorage.theme === 'dark') {
            applyDarkTheme();
        } else {
            applyLightTheme();
        }
    }

    // Bắt sự kiện Click chuyển đổi theme
    $darkModeToggle.on('click', function () {
        if ($('html').hasClass('dark')) {
            applyLightTheme();
        } else {
            applyDarkTheme();
        }
    });

    // Chạy cấu hình ban đầu
    initializeTheme();
});

// Quản lý các tương tác AJAX cho trang Luyện tập
$(document).ready(function () {
    // Phân tích tham số level trên URL để chọn sẵn Bloom level tương ứng
    const urlParams = new URLSearchParams(window.location.search);
    const urlLevel = urlParams.get('level');
    
    if (urlLevel) {
        let selectedValue = -1;
        if (urlLevel.toLowerCase() === 'remember') {
            selectedValue = 0;
        } else if (urlLevel.toLowerCase() === 'understand') {
            selectedValue = 1;
        } else if (urlLevel.toLowerCase() === 'apply') {
            selectedValue = 2;
        }
        
        if (selectedValue !== -1) {
            $('#bloom-level-select').val(selectedValue);
        }
    }
});

// Hiển thị/Ẩn loading spinner modal với hiệu ứng transition
function showLoading(show) {
    const $modal = $('#loading-modal');
    if (show) {
        $modal.removeClass('hidden').addClass('flex opacity-0');
        // Kích hoạt transition
        setTimeout(() => {
            $modal.addClass('opacity-100');
        }, 20);
    } else {
        $modal.removeClass('opacity-100').addClass('opacity-0');
        setTimeout(() => {
            $modal.addClass('hidden').removeClass('flex opacity-0');
        }, 300); // Khớp với duration-300
    }
}

// Gọi AJAX tạo đề và bắt đầu session luyện tập
function executePracticeFlow(levelValue, questionCount) {
    showLoading(true);
    
    $.ajax({
        url: '/Practice/CreatePracticeQuiz',
        type: 'POST',
        data: {
            targetLevel: levelValue,
            questionCount: questionCount
        },
        success: function (res) {
            if (res.success) {
                // Tạo session luyện tập
                $.ajax({
                    url: '/Practice/StartPracticeSession',
                    type: 'POST',
                    data: {
                        quizSetId: res.quizSetId
                    },
                    success: function (sessionRes) {
                        if (sessionRes.success) {
                            // Chuyển hướng sang trang thi thử với mode=practice
                            window.location.href = `/Exam/Index?quizSetId=${res.quizSetId}&id=${sessionRes.sessionId}&returnUrl=Practice`;
                        } else {
                            showLoading(false);
                            alert(sessionRes.message || "Không thể khởi tạo phiên làm bài luyện tập.");
                        }
                    },
                    error: function () {
                        showLoading(false);
                        alert("Có lỗi xảy ra khi bắt đầu làm bài luyện tập.");
                    }
                });
            } else {
                showLoading(false);
                alert(res.message || "Không thể tạo bộ đề luyện tập.");
            }
        },
        error: function () {
            showLoading(false);
            alert("Có lỗi kết nối xảy ra khi yêu cầu tạo đề luyện tập.");
        }
    });
}

// Bắt đầu luyện tập từ một card cụ thể
function startPractice(levelName, levelValue) {
    const questionCount = $('#question-count-select').val() || 10;
    executePracticeFlow(levelValue, parseInt(questionCount));
}

// Bắt đầu luyện tập từ bảng thiết lập bên phải
function startPracticeFromPanel() {
    const levelValue = $('#bloom-level-select').val();
    const questionCount = $('#question-count-select').val();
    
    if (levelValue === null || levelValue === undefined) {
        alert("Vui lòng chọn Bloom Level cần luyện tập.");
        return;
    }
    
    executePracticeFlow(parseInt(levelValue), parseInt(questionCount));
}

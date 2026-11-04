using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>Everything outside the settings window: option lists, the bar's menu and messages, the control center.</summary>
    static partial void AddApp(Dictionary<string, (string En, string Vi)> d)
    {
        // Each language names itself.
        d.Add("Lang_English", ("English", "English"));
        d.Add("Lang_Vietnamese", ("Tiếng Việt", "Tiếng Việt"));

        // ---- option lists
        d.Add("Opt_Layer_Auto", ("Automatic", "Tự động"));
        d.Add("Opt_Layer_Under", ("Under the taskbar icons", "Nằm dưới icon taskbar"));
        d.Add("Opt_Layer_Over", ("Tinted over the taskbar", "Phủ mờ lên taskbar"));
        d.Add("Opt_Place_Right", ("Right, next to the system tray", "Bên phải, cạnh khay hệ thống"));
        d.Add("Opt_Place_Left", ("Left", "Bên trái"));
        d.Add("Opt_Lyrics_BesideCover", ("Next to the cover", "Ngay cạnh ảnh bìa"));
        d.Add("Opt_Lyrics_LargestGap", ("Largest free space on the taskbar", "Khoảng trống lớn nhất trên taskbar"));
        d.Add("Opt_Size_Small", ("Small", "Nhỏ"));
        d.Add("Opt_Size_Medium", ("Medium", "Thường"));
        d.Add("Opt_Bg_Dark", ("Dark", "Tối"));
        d.Add("Opt_Bg_Tinted", ("Dark, tinted with the song's color", "Tối, pha màu bài hát"));
        d.Add("Opt_Bg_Transparent", ("Transparent (progress bar only)", "Trong suốt (chỉ có thanh tiến trình)"));
        d.Add("Opt_Ctl_Glow", ("Glow: play is a disc in the song's color", "Đĩa sáng: nút phát là đĩa màu bài hát"));
        d.Add("Opt_Ctl_Minimal", ("Minimal: icons only", "Tối giản: chỉ có biểu tượng"));
        d.Add("Opt_Ctl_Capsule", ("Capsule: all three in one glass pill", "Viên kẹo: ba nút chung một viên kính"));
        d.Add("Opt_Ctl_Ring", ("Progress ring: a ring that follows the song", "Vòng tiến trình: vòng chạy theo bài"));
        d.Add("Opt_Ctl_Vinyl", ("Vinyl: spins while playing, the cover is the label", "Đĩa than: xoay khi phát, nhãn là ảnh bìa"));
        d.Add("Opt_Ctl_Keys", ("Cassette keys: raised keys with a lamp", "Phím cassette: phím nổi, có đèn báo"));
        d.Add("Opt_Ctl_Bubbles", ("Bubbles: they wobble like jelly when pressed", "Bong bóng: bấm là nảy như thạch"));
        d.Add("Opt_Accent_Art", ("From the album cover", "Theo ảnh bìa bài hát"));
        d.Add("Opt_Accent_App", ("From the playing app's color", "Theo màu ứng dụng phát nhạc"));
        d.Add("Opt_Accent_System", ("Windows accent color", "Theo màu nhấn của Windows"));
        d.Add("Opt_Accent_Custom", ("Custom color", "Màu tự chọn"));
        d.Add("Opt_Align_Left", ("Next to the cover", "Sát ảnh bìa"));
        d.Add("Opt_Align_Center", ("Centered in the widget", "Giữa widget"));

        // ---- progress bar styles
        d.Add("Seek_Aurora", ("Aurora", "Cực quang"));
        d.Add("Seek_Aurora_Desc", ("Three ribbons of light in the song's colors, swelling with the music", "Ba dải sáng uốn lượn theo màu bài hát, dâng lên theo nhạc"));
        d.Add("Seek_Mesh", ("Color mesh", "Lưới màu"));
        d.Add("Seek_Mesh_Desc", ("Big patches of the cover's colors drift, blend and breathe with the music", "Những mảng màu lớn của ảnh bìa trôi chậm, hòa vào nhau và thở theo nhạc"));
        d.Add("Seek_Silk", ("Silk", "Sợi tơ"));
        d.Add("Seek_Silk_Desc", ("Fine threads twist around each other and twine into one at the playhead", "Những sợi tơ mảnh xoắn quanh nhau, se lại thành một sợi ở đầu phát"));
        d.Add("Seek_Holo", ("Holographic", "Ánh kim"));
        d.Add("Seek_Holo_Desc", ("Holographic foil shifting around the song's color, with light gliding across", "Giấy kim tuyến đổi sắc quanh màu bài hát, ánh sáng lướt qua mặt giấy"));
        d.Add("Seek_Bokeh", ("Bokeh", "Ánh đèn mờ"));
        d.Add("Seek_Bokeh_Desc", ("Out-of-focus lights near and far, each one pulsing with a frequency band", "Đốm đèn ngoài nét xa gần, mỗi đốm nhấp nháy theo một dải tần"));
        d.Add("Seek_Ink", ("Ink bloom", "Mực loang"));
        d.Add("Seek_Ink_Desc", ("Watercolor spreads behind the playhead, and every drop keeps blooming", "Màu nước loang ra sau đầu phát, từng giọt mực vẫn tiếp tục lan"));
        d.Add("Seek_Ribbon", ("Cover stripes", "Dải màu ảnh bìa"));
        d.Add("Seek_Ribbon_Desc", ("Each column of the cover becomes a stripe, revealed as the song plays", "Màu từng cột của ảnh bìa trải thành dải, lộ dần theo bài"));
        d.Add("Seek_Cinema", ("Cinema", "Điện ảnh"));
        d.Add("Seek_Cinema_Desc", ("The cover, deeply blurred, drifting slowly like a film scene", "Ảnh bìa nhòe sâu, trôi chậm như một cảnh phim"));
        d.Add("Seek_Neon", ("Neon tube", "Ống neon"));
        d.Add("Seek_Neon_Desc", ("A neon tube along the edge, flickering and casting light on the taskbar", "Ống đèn neon dọc mép, chập chờn và hắt sáng xuống taskbar"));
        d.Add("Seek_Liquid", ("Liquid", "Chất lỏng"));
        d.Add("Seek_Liquid_Desc", ("A rippling liquid surface at the playhead, with tiny bubbles rising", "Mặt chất lỏng gợn sóng ở đầu phát, bọt khí li ti nổi lên"));
        d.Add("Seek_Spectrum", ("Spectrum", "Phổ âm"));
        d.Add("Seek_Spectrum_Desc", ("A real audio spectrum, with the played part colored in", "Cột phổ âm thanh thật, phần đã phát được tô màu"));
        d.Add("Seek_Waveform", ("Waveform", "Dạng sóng"));
        d.Add("Seek_Waveform_Desc", ("A SoundCloud-style waveform, drawn from the real sound as it plays", "Sóng âm kiểu SoundCloud, tự vẽ dạng sóng thật khi phát"));
        d.Add("Seek_Mosaic", ("LED grid", "Ô LED"));
        d.Add("Seek_Mosaic_Desc", ("A grid of squares sparkling in the song's palette", "Ma trận ô vuông lấp lánh theo bảng màu bài hát"));
        d.Add("Seek_Classic", ("Classic", "Cổ điển"));
        d.Add("Seek_Classic_Desc", ("A color fill with a glow at the playhead", "Phủ màu và quầng sáng ở đầu phát"));

        // ---- lyric effects
        d.Add("Fx_SlideUp", ("Slide up", "Cuộn lên (Slide up)"));
        d.Add("Fx_Fade", ("Fade", "Mờ dần (Fade)"));
        d.Add("Fx_Swipe", ("Swipe", "Quét ngang (Swipe)"));
        d.Add("Fx_Zoom", ("Zoom blur", "Phóng to + nhòe (Zoom blur)"));
        d.Add("Fx_Words", ("Word pop", "Từng chữ nảy lên (Word pop)"));
        d.Add("Fx_Typewriter", ("Typewriter", "Gõ chữ (Typewriter)"));
        d.Add("Fx_Ticker", ("Ticker", "Băng chữ chạy ngang (Ticker)"));
        d.Add("Fx_None", ("No effect", "Không hiệu ứng"));

        // ---- the bar: messages, song card, right-click menu
        d.Add("Msg_NothingPlaying", ("Nothing is playing", "Không có nhạc đang phát"));
        d.Add("Msg_Instrumental", ("♪ Instrumental", "♪ Nhạc không lời"));
        d.Add("Card_Track", ("Track {0}", "Bài {0}"));
        d.Add("Card_TrackOf", ("Track {0}/{1}", "Bài {0}/{1}"));
        d.Add("Menu_OpenApp", ("Open {0}", "Mở {0}"));
        d.Add("Menu_ShowLyrics", ("Show lyrics next to the cover", "Hiện lời bài hát cạnh ảnh bìa"));
        d.Add("Menu_Source", ("Player", "Nguồn phát"));
        d.Add("Menu_SourceAuto", ("Automatic (whatever is playing)", "Tự động (ưu tiên đang phát)"));
        d.Add("Menu_Lyrics", ("Lyrics", "Lời bài hát"));
        d.Add("Menu_LyricsStatus", ("Status: {0}", "Trạng thái: {0}"));
        d.Add("Menu_Reload", ("Reload from LRCLIB", "Tải lại từ LRCLIB"));
        d.Add("Menu_Earlier", ("Show lyrics 0.25 s earlier", "Hiện lời sớm hơn 0,25 giây"));
        d.Add("Menu_Later", ("Show lyrics 0.25 s later", "Hiện lời trễ hơn 0,25 giây"));
        d.Add("Menu_ResetOffset", ("Reset this song's offset ({0} ms)", "Đặt lại độ lệch bài này ({0} ms)"));
        d.Add("Menu_LyricsFolder", ("Open the lyrics folder", "Mở thư mục lời bài hát"));
        d.Add("Menu_LyricEffect", ("Lyric effect", "Hiệu ứng lời"));
        d.Add("Menu_SeekStyle", ("Progress bar style", "Kiểu thanh tiến trình"));
        d.Add("Menu_ControlStyle", ("Button style", "Kiểu nút điều khiển"));
        d.Add("Menu_Settings", ("Settings…", "Cài đặt…"));
        d.Add("Menu_Exit", ("Exit MusicBar", "Thoát MusicBar"));
        d.Add("LyrSt_Loading", ("searching…", "đang tìm…"));
        d.Add("LyrSt_Downloaded", ("just downloaded from LRCLIB", "vừa tải từ LRCLIB"));
        d.Add("LyrSt_Demo", ("demo lyrics", "lời demo"));
        d.Add("LyrSt_Cached", ("saved on this PC", "lấy từ bộ nhớ máy"));
        d.Add("LyrSt_PlainOnly", ("unsynced lyrics only", "chỉ có lời không đồng bộ"));
        d.Add("LyrSt_Instrumental", ("instrumental", "nhạc không lời"));
        d.Add("LyrSt_NotFound", ("not found", "không tìm thấy"));
        d.Add("LyrSt_Error", ("network error, trying again later", "lỗi mạng, thử lại sau"));
        d.Add("LyrSt_None", ("none yet", "chưa có"));
        d.Add("LyrSt_Off", ("turned off", "đang tắt"));

        // ---- control center
        d.Add("CC_Title", ("Control center", "Trung tâm điều khiển"));
        d.Add("CC_Notifications", ("Notifications", "Thông báo"));
        d.Add("CC_ClearAll", ("Clear all", "Xóa tất cả"));
        d.Add("CC_NoNotifications", ("No new notifications", "Không có thông báo mới"));
        d.Add("CC_NotificationsDenied", ("Windows hasn't let MusicBar read notifications", "Windows chưa cho MusicBar đọc thông báo"));
        d.Add("CC_DismissOne", ("Dismiss this notification", "Bỏ thông báo này"));
        d.Add("CC_JustNow", ("Just now", "Vừa xong"));
        d.Add("CC_MinutesAgo", ("{0} min", "{0} phút"));
        d.Add("CC_Yesterday", ("Yesterday", "Hôm qua"));
        d.Add("CC_DayMonth", ("M/d", "dd/MM"));
        d.Add("CC_Mute", ("Mute / unmute", "Tắt / bật tiếng"));
        d.Add("CC_ChooseOutput", ("Choose a playback device", "Chọn thiết bị phát"));
        d.Add("CC_NoOutput", ("No playback device", "Không có thiết bị phát"));
        d.Add("CC_Outputs", ("Sound output", "Đầu ra âm thanh"));
        d.Add("CC_NoOutputs", ("No playback devices found.", "Không tìm thấy thiết bị phát nào."));
        d.Add("CC_Project", ("Project", "Màn hình chiếu"));
        d.Add("CC_PcOnly", ("PC screen only", "Chỉ màn hình máy tính"));
        d.Add("CC_PcOnly_Desc", ("Turn off the second screen", "Tắt màn hình thứ hai"));
        d.Add("CC_Duplicate", ("Duplicate", "Nhân bản"));
        d.Add("CC_Duplicate_Desc", ("Both screens show the same thing", "Hai màn hình hiện giống nhau"));
        d.Add("CC_Extend", ("Extend", "Mở rộng"));
        d.Add("CC_Extend_Desc", ("Drag windows onto the second screen", "Kéo cửa sổ sang màn hình thứ hai"));
        d.Add("CC_SecondOnly", ("Second screen only", "Chỉ màn hình thứ hai"));
        d.Add("CC_SecondOnly_Desc", ("Turn off the main screen", "Tắt màn hình chính"));
        d.Add("CC_ProjectFailed", ("Windows couldn't switch to this mode (a second screen needs to be plugged in).", "Windows không đổi được sang chế độ này (cần có màn hình thứ hai đang cắm)."));
        d.Add("CC_Cast", ("Cast", "Truyền màn hình"));
        d.Add("CC_Disconnect", ("Disconnect", "Ngắt kết nối"));
        d.Add("CC_Casting", ("Casting to a wireless display", "Đang truyền tới màn hình không dây"));
        d.Add("CC_Searching", ("Looking for wireless displays…", "Đang tìm màn hình không dây…"));
        d.Add("CC_NoDisplays", ("No wireless displays found. Turn on Miracast receiving on the TV or display, then open this again.", "Không tìm thấy màn hình không dây nào. Bật chế độ nhận Miracast trên TV hoặc màn hình rồi mở lại."));
        d.Add("CC_NoWifi", ("Casting with Miracast needs Wi-Fi, and this PC has no Wi-Fi adapter.", "Truyền màn hình qua Miracast cần Wi-Fi; máy chưa cắm bộ thu Wi-Fi nào."));
        d.Add("CC_Connecting", ("Connecting to {0}…", "Đang kết nối tới {0}…"));
        d.Add("CC_ConnectFailed", ("Couldn't connect to {0}.", "Không kết nối được tới {0}."));
        d.Add("QA_NightLight", ("Night light", "Ánh sáng đêm"));
        d.Add("QA_LiveCaptions", ("Live captions", "Phụ đề trực tiếp"));
        d.Add("QA_EnergySaver", ("Energy saver", "Tiết kiệm năng lượng"));
        d.Add("QA_NearbySharing", ("Nearby sharing", "Chia sẻ lân cận"));
        d.Add("QA_Project", ("Project", "Màn hình chiếu"));
        d.Add("QA_Cast", ("Cast", "Truyền màn hình"));
        d.Add("QA_Accessibility", ("Accessibility", "Trợ năng"));
        d.Add("Audio_Device", ("Audio device", "Thiết bị âm thanh"));

        d.Add("Taskbar_ExplorerPatcher", ("Windows 10 style (ExplorerPatcher)", "kiểu Windows 10 (ExplorerPatcher)"));
    }
}

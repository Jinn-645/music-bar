using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>The settings window.</summary>
    static partial void AddSettings(Dictionary<string, (string En, string Vi)> d)
    {
        d.Add("Set_Title", ("MusicBar Settings", "Cài đặt MusicBar"));
        d.Add("Set_Version", ("Version {0}", "Phiên bản {0}"));
        d.Add("Set_Language", ("Language", "Ngôn ngữ"));
        d.Add("Set_Off", ("Off", "Tắt"));
        d.Add("Set_On", ("On", "Bật"));
        d.Add("Set_NowPlaying", ("NOW PLAYING", "ĐANG PHÁT"));
        d.Add("Set_NowPlaying_None", ("Nothing playing", "Chưa có gì"));
        d.Add("Set_NowPlaying_Hint", ("Open a music player", "Hãy mở một trình phát nhạc"));

        // ---- pages (navigation and page titles)
        d.Add("Set_Page_Bar", ("Taskbar", "Taskbar"));
        d.Add("Set_Page_Look", ("Appearance", "Giao diện"));
        d.Add("Set_Page_Lyrics", ("Lyrics", "Lời bài hát"));
        d.Add("Set_Page_About", ("About", "Giới thiệu"));

        // ---- taskbar page
        d.Add("Set_Bar_Subtitle", ("MusicBar runs right on the Windows taskbar: a progress layer that spans the full width of the taskbar, plus a widget with lyrics and playback buttons in the free space.", "MusicBar chạy ngay trên taskbar của Windows: một lớp thanh tiến trình trải hết chiều ngang taskbar, cùng một widget lời bài hát và nút điều khiển ở vùng trống."));
        d.Add("Set_Bar_GroupWindows", ("Windows taskbar", "Taskbar của Windows"));
        d.Add("Set_TaskbarSize", ("Taskbar size", "Kích thước taskbar"));
        d.Add("Set_TaskbarSize_Desc", ("Small taskbar: smaller icons and a thinner bar.", "Taskbar nhỏ: icon nhỏ và thanh mỏng hơn."));
        d.Add("Set_TaskbarSize_DescWin11", ("Uses the Windows 11 taskbar size setting: small means smaller icons and a thinner bar, applied right away.", "Dùng cài đặt Kích thước taskbar của Windows 11: nhỏ là icon nhỏ và thanh mỏng hơn, áp dụng ngay."));
        d.Add("Set_TaskbarSize_DescIconsOnly", ("This version of Windows 11 can only shrink the icons; the 48 px height is fixed.", "Bản Windows 11 này chỉ thu nhỏ được icon, chiều cao 48 px là cố định."));
        d.Add("Set_Transparent", ("Make the taskbar transparent", "Làm taskbar trong suốt"));
        d.Add("Set_Transparent_Desc", ("Built into MusicBar, no need to install TranslucentTB. When you exit MusicBar, the taskbar gets its background back.", "Có sẵn trong MusicBar, không cần cài TranslucentTB. Thoát MusicBar là taskbar có lại nền như cũ."));
        d.Add("Set_Transparent_DescTranslucentTB", ("TranslucentTB is running, so MusicBar leaves this to it.", "TranslucentTB đang chạy nên MusicBar để nó lo phần này."));
        d.Add("Set_ControlCenter", ("MusicBar control center", "Trung tâm điều khiển MusicBar"));
        d.Add("Set_ControlCenter_Desc", ("The network / volume button (Windows 11) or the Control Center button (ExplorerPatcher) opens MusicBar's panel: notifications, quick settings, volume and speaker choice. On a Windows 10-style taskbar, the volume icon and the notification bell are folded into it. Exit MusicBar and everything goes back to how it was.", "Nút mạng / âm lượng (Windows 11) hoặc nút Control Center (ExplorerPatcher) mở bảng của MusicBar: thông báo, cài đặt nhanh, âm lượng và chọn loa. Trên taskbar kiểu Windows 10, icon âm lượng và chuông thông báo được gộp vào đó. Thoát MusicBar là mọi thứ trở lại như cũ."));
        d.Add("Set_Center", ("Center the Start button and icons", "Đặt nút Start và icon ở giữa"));
        d.Add("Set_Center_Desc", ("The Start menu opens in the middle of the screen too.", "Start menu cũng mở ở giữa màn hình."));
        d.Add("Set_UseExplorerPatcher", ("Use the ExplorerPatcher taskbar", "Dùng taskbar ExplorerPatcher"));
        d.Add("Set_RestartExplorer", ("Restart Explorer", "Khởi động lại Explorer"));
        d.Add("Set_Tb_Current", ("Current taskbar: {0}.", "Taskbar hiện tại: {0}."));
        d.Add("Set_Tb_NoCenter", ("Windows 10 can't center the icons on its own; that takes ExplorerPatcher or Windows 11.", "Windows 10 không tự căn giữa icon được; cần ExplorerPatcher hoặc Windows 11."));
        d.Add("Set_Tb_TryExplorerPatcher", ("For a truly thin bar, use the ExplorerPatcher taskbar (already installed on this PC).", "Muốn thanh mỏng thật sự, dùng taskbar của ExplorerPatcher (máy bạn đã cài)."));
        d.Add("Set_Tb_PendingRestart", ("Changes saved; restart Explorer for the taskbar to apply them (open File Explorer windows will close).", "Đã lưu thay đổi; khởi động lại Explorer để taskbar áp dụng (các cửa sổ File Explorer đang mở sẽ đóng)."));
        d.Add("Set_Tb_RestartNote", ("Some changes only apply after Explorer restarts.", "Một số thay đổi chỉ áp dụng sau khi khởi động lại Explorer."));
        d.Add("Set_Restart_Ask", ("Restart Explorer so the taskbar applies the changes?", "Khởi động lại Explorer để taskbar áp dụng thay đổi?"));
        d.Add("Set_Restart_Warning", ("The taskbar will disappear for a few seconds and open File Explorer windows will close.", "Taskbar sẽ biến mất vài giây và các cửa sổ File Explorer đang mở sẽ đóng."));
        d.Add("Set_SwitchEp_Ask", ("Switch to ExplorerPatcher's Windows 10-style taskbar?\nThis taskbar can be made thin (small icons, about 30 px tall) and can still be centered.", "Chuyển sang taskbar kiểu Windows 10 của ExplorerPatcher?\nTaskbar này mỏng được (icon nhỏ, cao khoảng 30 px) và vẫn căn giữa được."));
        d.Add("Set_SwitchEp_Caption", ("ExplorerPatcher taskbar", "Taskbar ExplorerPatcher"));
        d.Add("Set_SwitchEp_Restart", ("Explorer needs to restart to switch taskbars. Restart now?", "Cần khởi động lại Explorer để đổi taskbar. Khởi động lại ngay?"));

        d.Add("Set_Bar_GroupLayer", ("Progress layer", "Lớp tiến trình"));
        d.Add("Set_LayerMode", ("Layer position relative to the taskbar", "Vị trí lớp so với taskbar"));
        d.Add("Set_LayerMode_Desc", ("Under: the taskbar icons sit on top of the layer, which needs a transparent taskbar (e.g. TranslucentTB). Tinted over: for a taskbar that isn't transparent; the opacity is capped so the icons stay clear.", "Nằm dưới: icon taskbar ở trên lớp, cần taskbar trong suốt (vd. TranslucentTB). Phủ mờ: dùng khi taskbar không trong suốt, độ đậm tự giới hạn để icon vẫn rõ."));
        d.Add("Set_Layer_Status", ("Currently: {0} ({1}).", "Đang dùng: {0} ({1})."));
        d.Add("Set_Layer_Over", ("tinted over the taskbar", "phủ mờ lên taskbar"));
        d.Add("Set_Layer_Under", ("under the taskbar icons", "nằm dưới icon taskbar"));
        d.Add("Set_Layer_ToolFound", ("TranslucentTB detected", "đã thấy TranslucentTB"));
        d.Add("Set_Layer_ToolMissing", ("no taskbar transparency tool detected", "không thấy công cụ làm trong suốt taskbar"));

        d.Add("Set_Bar_GroupWidget", ("Control widget", "Widget điều khiển"));
        d.Add("Set_WidgetPlacement", ("Widget position", "Vị trí widget"));
        d.Add("Set_WidgetPlacement_Desc", ("The widget finds the free space between the icons and the system tray by itself.", "Widget tự tìm khoảng trống giữa các icon và khay hệ thống."));
        d.Add("Set_LyricsExpanded", ("Show lyrics on the taskbar", "Hiện lời bài hát trên taskbar"));
        d.Add("Set_LyricsExpanded_Desc", ("Click the cover on the taskbar to quickly turn this on or off.", "Bấm vào ảnh bìa trên taskbar để bật hoặc tắt nhanh."));
        d.Add("Set_LyricsPlacement", ("Lyrics position", "Lời bài hát nằm ở"));
        d.Add("Set_LyricsPlacement_Desc", ("Largest free space: the lyrics follow the icons and sit in the middle of the free space, whether the icons are centered or left-aligned.", "Khoảng trống lớn nhất: lời tự dời theo icon, nằm giữa vùng trống dù icon căn giữa hay căn trái."));
        d.Add("Set_InfoPopup", ("Song info card", "Thẻ thông tin bài hát"));
        d.Add("Set_InfoPopup_Desc", ("Hover over the cover for a moment to see a large cover, the artist, album and genre.", "Giữ chuột trên ảnh bìa một lúc để xem ảnh bìa lớn, ca sĩ, album, thể loại."));
        d.Add("Set_OnlineInfo", ("Look up genre and release year", "Tra thể loại và năm phát hành"));
        d.Add("Set_OnlineInfo_Desc", ("Searches the iTunes catalog by song title and artist, since most players don't send a genre.", "Hỏi danh mục iTunes theo tên bài và ca sĩ, vì đa số trình phát không gửi thể loại."));
        d.Add("Set_WidgetMaxWidth", ("Width with lyrics shown", "Độ rộng khi mở lời bài hát"));
        d.Add("Set_Source", ("Tracked app", "Ứng dụng được theo dõi"));
        d.Add("Set_Source_Desc", ("Automatic picks the app that's playing; pin an app to always show it.", "Tự động sẽ chọn ứng dụng đang phát; ghim một ứng dụng để luôn hiện nó."));
        d.Add("Set_Source_Auto", ("Automatic (whatever is playing)", "Tự động (ưu tiên đang phát)"));
        d.Add("Set_Source_NotRunning", ("{0} (not running)", "{0} (chưa chạy)"));

        d.Add("Set_Bar_GroupBehavior", ("Behavior", "Hành vi"));
        d.Add("Set_HideIdle", ("Hide when no music is playing", "Ẩn khi không có nhạc"));
        d.Add("Set_HideIdle_Desc", ("The taskbar goes back to normal; MusicBar shows up again when an app starts playing.", "Taskbar trở về như cũ; MusicBar tự hiện lại khi một ứng dụng bắt đầu phát."));
        d.Add("Set_HideFullscreen", ("Hide when an app is full screen", "Ẩn khi có ứng dụng toàn màn hình"));
        d.Add("Set_HideFullscreen_Desc", ("Games, full-screen video, presentations...", "Game, video toàn màn hình, trình chiếu..."));
        d.Add("Set_StartWithWindows", ("Start with Windows", "Khởi động cùng Windows"));

        // ---- appearance page
        d.Add("Set_Look_Subtitle", ("Colors, the taskbar background and what the widget shows. Changes apply right away.", "Màu sắc, nền taskbar và những thứ hiển thị trong widget. Mọi thay đổi áp dụng ngay."));
        d.Add("Set_Look_GroupColors", ("Background and color", "Nền và màu"));
        d.Add("Set_Background", ("Taskbar background", "Nền taskbar"));
        d.Add("Set_BgOpacity", ("Background opacity", "Độ đậm nền"));
        d.Add("Set_BgOpacity_Desc", ("Doesn't apply to the transparent background. Capped at 35% when tinted over the taskbar.", "Không áp dụng cho nền trong suốt. Ở chế độ phủ mờ tối đa 35%."));
        d.Add("Set_Accent", ("Progress bar accent color", "Màu nhấn của thanh tiến trình"));
        d.Add("Set_Accent_Desc", ("If the cover has no standout color, the app's color is used, then the Windows accent color.", "Ảnh bìa không có màu nổi bật thì dùng màu ứng dụng, rồi tới màu Windows."));
        d.Add("Set_CustomColor", ("Custom color", "Màu tự chọn"));
        d.Add("Set_CustomColor_Desc", ("Pick a swatch or enter a hex code.", "Chọn một mẫu hoặc nhập mã hex."));
        d.Add("Set_Look_GroupStyle", ("Progress bar style", "Phong cách thanh tiến trình"));
        d.Add("Set_Look_StyleHint", ("The previews run live with the colors of the song that's playing. Click one to choose it.", "Bản xem trước chạy thật với màu của bài đang phát. Bấm để chọn."));
        d.Add("Set_FillOpacity", ("Opacity", "Độ đậm"));
        d.Add("Set_AudioReactive", ("Move with the music", "Nhảy theo nhạc"));
        d.Add("Set_AudioReactive_Desc", ("Listens to the sound going to your speakers so styles like Spectrum, Waveform and Aurora move with the music. Processed only on this PC.", "Đọc âm thanh đang phát ra loa để các kiểu như Phổ âm, Dạng sóng, Cực quang chuyển động theo nhạc. Chỉ xử lý trong máy."));
        d.Add("Set_Look_GroupParts", ("Elements", "Thành phần"));
        d.Add("Set_AlbumArt", ("Album cover", "Ảnh bìa"));
        d.Add("Set_AppIcon", ("Icon of the playing app", "Biểu tượng ứng dụng đang phát"));
        d.Add("Set_AppIcon_Desc", ("A small badge in the corner of the cover.", "Huy hiệu nhỏ ở góc ảnh bìa."));
        d.Add("Set_Controls", ("Playback buttons (previous / play / next)", "Nút điều khiển (lùi / phát / tới)"));
        d.Add("Set_ControlStyle", ("Button style", "Kiểu nút điều khiển"));
        d.Add("Set_ControlStyle_Desc", ("Changes show on the taskbar right away. You can also change it by right-clicking the widget.", "Đổi là thấy ngay trên taskbar. Chuột phải lên widget cũng đổi được."));
        d.Add("Set_Font", ("Font", "Phông chữ"));
        d.Add("Set_Font_Desc", ("The first 10 fonts in the list come with MusicBar; they're free and fully support Vietnamese.", "10 phông đầu danh sách có sẵn trong MusicBar, miễn phí và hỗ trợ đủ tiếng Việt."));

        // ---- lyrics page
        d.Add("Set_Lyr_Subtitle", ("Line-synced lyrics come from LRCLIB (an open lyrics library). Downloaded lyrics are saved as .lrc files, ready to use next time without a connection.", "Lời đồng bộ theo từng dòng lấy từ LRCLIB (kho lời mở). Lời đã tải được lưu thành file .lrc để lần sau dùng ngay, không cần mạng."));
        d.Add("Set_Lyr_GroupDisplay", ("Display", "Hiển thị"));
        d.Add("Set_LyricsEnabled", ("Show lyrics", "Hiện lời bài hát"));
        d.Add("Set_LyricEffect", ("Line transition effect", "Hiệu ứng chuyển dòng"));
        d.Add("Set_Preview", ("Preview", "Xem thử"));
        d.Add("Set_AnimSpeed", ("Effect speed", "Tốc độ hiệu ứng"));
        d.Add("Set_Karaoke", ("Karaoke highlight in time", "Tô màu karaoke theo thời gian"));
        d.Add("Set_Karaoke_Desc", ("The line being sung fills in from left to right.", "Dòng đang hát được tô dần từ trái sang phải."));
        d.Add("Set_NextLine", ("Show the next line", "Hiện dòng kế tiếp"));
        d.Add("Set_NextLine_Desc", ("Needs a taskbar at least 40 px tall (medium icons).", "Cần taskbar cao từ 40 px (icon cỡ thường)."));
        d.Add("Set_LyricsAlign", ("Lyrics alignment", "Căn lề lời"));
        d.Add("Set_Offset", ("Global timing offset", "Độ lệch thời gian chung"));
        d.Add("Set_Offset_Desc", ("Positive values show lyrics earlier. Each song can be adjusted on its own by right-clicking the bar.", "Số dương: lời hiện sớm hơn. Từng bài có thể chỉnh riêng bằng chuột phải lên thanh."));
        d.Add("Set_Lyr_GroupText", ("Text style", "Kiểu chữ"));
        d.Add("Set_LyricScale", ("Lyrics text size", "Cỡ chữ lời"));
        d.Add("Set_LyricColor", ("Text color", "Màu chữ"));
        d.Add("Set_LyricColor_Desc", ("In karaoke, the sung part is tinted with the song's color.", "Phần đã hát trong karaoke pha thêm màu của bài."));
        d.Add("Set_LyricOpacity", ("Text opacity", "Độ đậm chữ"));
        d.Add("Set_LyricOpacity_Desc", ("The next line and the part not yet sung fade along with it.", "Dòng kế tiếp và phần chưa hát nhạt hơn theo."));
        d.Add("Set_Stroke", ("Text outline", "Viền chữ"));
        d.Add("Set_Stroke_Desc", ("A colored halo around each letter so the lyrics stand out on the progress bar.", "Quầng màu quanh từng chữ để lời nổi trên thanh tiến trình."));
        d.Add("Set_StrokeColor", ("Outline color", "Màu viền"));
        d.Add("Set_StrokeOpacity", ("Outline opacity", "Độ đậm viền"));
        d.Add("Set_StrokeRadius", ("Outline width", "Độ rộng viền"));
        d.Add("Set_StrokeRadius_Desc", ("Small gives a crisp outline around the text, large a soft glow.", "Nhỏ là viền sắc quanh chữ, lớn là quầng sáng mềm."));
        d.Add("Set_Lyr_GroupSource", ("Source and storage", "Nguồn và bộ nhớ"));
        d.Add("Set_FetchOnline", ("Download lyrics from LRCLIB", "Tải lời từ LRCLIB"));
        d.Add("Set_FetchOnline_Desc", ("Turn off to use only .lrc files already on this PC.", "Tắt để chỉ dùng file .lrc có sẵn trong máy."));
        d.Add("Set_Proxy", ("Proxy", "Proxy"));
        d.Add("Set_Proxy_Desc", ("Leave empty to use the Windows network settings. Example: http://127.0.0.1:1080", "Để trống để dùng cài đặt mạng của Windows. Ví dụ: http://127.0.0.1:1080"));
        d.Add("Set_LyricsFolder", ("Lyrics folder", "Thư mục lưu lời"));
        d.Add("Set_LyricsFolder_Desc", ("Put your own \"Artist - Title.lrc\" files here and MusicBar will use them first.", "Bỏ file \"Ca sĩ - Tên bài.lrc\" của bạn vào đây, MusicBar sẽ ưu tiên dùng."));
        d.Add("Set_PickLyricsFolder", ("Choose a folder for lyrics", "Chọn thư mục lưu lời bài hát"));
        d.Add("Set_Open", ("Open", "Mở"));
        d.Add("Set_Change", ("Change…", "Đổi…"));
        d.Add("Set_Default", ("Default", "Mặc định"));
        d.Add("Set_Cache", ("Cache", "Bộ nhớ đệm"));
        d.Add("Set_Cache_Desc", ("Songs saved: {0} · Marked not found: {1} (tried again automatically after 3 days)", "{0} bài đã lưu · {1} bài đánh dấu không tìm thấy (tự thử lại sau 3 ngày)"));
        d.Add("Set_RetryMisses", ("Retry songs not found", "Thử tìm lại bài lỗi"));
        d.Add("Set_ClearDownloaded", ("Delete downloaded lyrics", "Xóa lời đã tải"));
        d.Add("Set_ClearDownloaded_Ask", ("Delete all lyrics MusicBar has downloaded?\nAny .lrc files you added to the folder yourself will be kept.", "Xóa toàn bộ lời bài hát MusicBar đã tải về?\nFile .lrc bạn tự thêm vào thư mục sẽ được giữ nguyên."));

        // ---- about page
        d.Add("Set_About_Subtitle", ("Turns the Windows taskbar into a music bar: song progress runs under the icons, with synced lyrics and playback buttons right on the taskbar.", "Biến taskbar Windows thành thanh nhạc: tiến trình bài hát chạy dưới các icon, lời bài hát đồng bộ và nút điều khiển ngay trên taskbar."));
        d.Add("Set_About_Lyrics", ("Lyrics:", "Lời bài hát:"));
        d.Add("Set_About_Media", ("Playback control:", "Điều khiển nhạc:"));
        d.Add("Set_About_GroupTips", ("Tips", "Mẹo"));
        d.Add("Set_Tip_Bar", ("The whole taskbar is the progress bar", "Cả taskbar là thanh tiến trình"));
        d.Add("Set_Tip_Bar_Desc", ("The played part is colored in from the left edge of the taskbar. The layer ignores the mouse, so the taskbar works as usual.", "Phần đã phát phủ màu từ mép trái taskbar. Lớp này không nhận chuột, taskbar dùng như bình thường."));
        d.Add("Set_Tip_RightClick", ("Right-click the widget", "Chuột phải lên widget"));
        d.Add("Set_Tip_RightClick_Desc", ("Switch the player, reload lyrics, shift lyrics earlier or later for each song, and quickly change the effect.", "Đổi nguồn phát, tải lại lời, chỉnh lời sớm/trễ cho từng bài, đổi nhanh hiệu ứng."));
        d.Add("Set_Tip_ClickCover", ("Click the cover", "Nhấp vào ảnh bìa"));
        d.Add("Set_Tip_ClickCover_Desc", ("Shows or hides the lyrics next to it.", "Mở hoặc thu gọn lời bài hát ngay cạnh ảnh bìa."));
        d.Add("Set_About_GroupData", ("Data", "Dữ liệu"));
        d.Add("Set_DataFolder", ("Data folder", "Thư mục dữ liệu"));
        d.Add("Set_DataFolder_Desc", ("Settings, logs and, by default, lyrics.", "Cài đặt, nhật ký và lời bài hát mặc định."));
        d.Add("Set_OpenFolder", ("Open folder", "Mở thư mục"));
        d.Add("Set_ViewLog", ("View log", "Xem log"));
    }
}

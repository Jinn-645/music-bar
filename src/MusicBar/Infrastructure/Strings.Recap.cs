using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>The year in music: its card and the end-of-year banner.</summary>
    static partial void AddRecap(Dictionary<string, (string En, string Vi)> d)
    {
        d.Add("Menu_Recap", ("Your year in music…", "Tổng kết năm âm nhạc…"));

        d.Add("Rec_WindowTitle", ("MusicBar: your year in music", "MusicBar: tổng kết năm âm nhạc"));
        d.Add("Rec_Kicker", ("MUSICBAR · YEAR IN MUSIC", "MUSICBAR · TỔNG KẾT NĂM"));
        d.Add("Rec_Subtitle", ("Your year, song by song", "Một năm của bạn, qua từng bài hát"));
        d.Add("Rec_HoursUnit", ("hours of music", "giờ nghe nhạc"));
        d.Add("Rec_MinutesUnit", ("minutes of music", "phút nghe nhạc"));
        d.Add("Rec_HoursMinutes", ("{0} h {1} min", "{0} giờ {1} phút"));
        d.Add("Rec_Minutes", ("{0} min", "{0} phút"));
        d.Add("Rec_Counts", ("{0} plays · {1} songs · {2} artists", "{0} lượt nghe · {1} bài · {2} nghệ sĩ"));
        d.Add("Rec_TopSongs", ("TOP SONGS", "TOP BÀI HÁT"));
        d.Add("Rec_TopArtists", ("ARTISTS YOU LOVED", "NGHỆ SĨ RUỘT"));
        d.Add("Rec_Plays", ("{0} plays", "{0} lượt"));
        d.Add("Rec_BigDay", ("BIGGEST DAY", "NGÀY CHÁY NHẤT"));
        d.Add("Rec_GoldenHour", ("GOLDEN HOUR", "GIỜ VÀNG"));
        d.Add("Rec_GoldenHourSub", ("until {0}:00", "đến {0}:00"));
        d.Add("Rec_Streak", ("LONGEST STREAK", "CHUỖI DÀI NHẤT"));
        d.Add("Rec_Days", ("{0} days", "{0} ngày"));
        d.Add("Rec_DaysListened", ("{0} days with music", "{0} ngày có nhạc"));
        d.Add("Rec_App", ("MOSTLY ON", "NGHE NHIỀU NHẤT"));
        d.Add("Rec_YouAre", ("YOU ARE", "BẠN LÀ"));
        d.Add("Rec_Kind_AllRounder", ("The all-rounder", "Người nghe đa năng"));
        d.Add("Rec_KindLine_AllRounder", ("Morning, noon or night, there's always music on.", "Sáng trưa chiều tối, lúc nào cũng có nhạc."));
        d.Add("Rec_Kind_NightOwl", ("Night owl", "Cú đêm"));
        d.Add("Rec_KindLine_NightOwl", ("Most of your music plays after ten at night.", "Phần lớn nhạc của bạn chạy sau 10 giờ tối."));
        d.Add("Rec_Kind_EarlyBird", ("Early bird", "Chim sớm"));
        d.Add("Rec_KindLine_EarlyBird", ("The music's on before the sun is fully up.", "Trời chưa sáng hẳn là bạn đã mở nhạc rồi."));
        d.Add("Rec_Kind_DayShift", ("Nine-to-five groover", "Dân văn phòng chill"));
        d.Add("Rec_KindLine_DayShift", ("Music is your favourite coworker.", "Nhạc là đồng nghiệp ruột trong giờ làm của bạn."));
        d.Add("Rec_Kind_OnRepeat", ("On repeat", "Fan cứng nghe lặp"));
        d.Add("Rec_KindLine_OnRepeat", ("Once you love a song, you play it to pieces.", "Đã thích bài nào là nghe tới mòn bài đó."));
        d.Add("Rec_Kind_Explorer", ("Explorer", "Thợ săn nhạc mới"));
        d.Add("Rec_KindLine_Explorer", ("Always hunting for the next new song.", "Lúc nào cũng đi tìm bài mới, ít khi nghe lại."));
        d.Add("Rec_Empty", ("Nothing counted for {0} yet. Play some music and come back!", "Chưa đếm được bài nào trong năm {0}. Nghe nhạc rồi quay lại nhé!"));
        d.Add("Rec_Note", ("Counted on this computer only. Share it if you like!", "Chỉ đếm trên máy này. Thích thì khoe với ae!"));
        d.Add("Rec_Save", ("Save picture", "Lưu ảnh"));
        d.Add("Rec_Copy", ("Copy", "Copy ảnh"));
        d.Add("Rec_Saved", ("Saved. Ready to post!", "Lưu rồi, đăng thôi!"));
        d.Add("Rec_SaveFailed", ("Couldn't save the picture.", "Không lưu được ảnh."));
        d.Add("Rec_Copied", ("Copied. Paste it into a post (Ctrl+V).", "Đã copy, dán vào bài đăng là được (Ctrl+V)."));

        d.Add("RecB_Title", ("Your {0} in music is ready", "Tổng kết năm {0} của bạn đã có"));
        d.Add("RecB_Body", ("{0} of music this year. See your top songs and what kind of listener you are.", "Năm nay bạn nghe {0} nhạc. Xem top bài hát và bạn thuộc kiểu người nghe nào nhé."));
        d.Add("RecB_Open", ("Show me", "Xem ngay"));
        d.Add("RecB_Later", ("Later", "Để sau"));
        d.Add("RecB_Never", ("Don't remind me this year", "Không nhắc lại năm nay"));
    }
}

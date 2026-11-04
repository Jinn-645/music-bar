using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>The lyrics panel, its search, and romanization.</summary>
    static partial void AddLyrics(Dictionary<string, (string En, string Vi)> d)
    {
        d.Add("Menu_LyricsPanel", ("Whole song's lyrics…", "Lời cả bài…"));

        d.Add("LyrP_WindowTitle", ("Lyrics", "Lời bài hát"));
        d.Add("LyrP_Close", ("Close (Esc)", "Đóng (Esc)"));
        d.Add("LyrP_SeekTip", ("Play from this line", "Phát từ dòng này"));
        d.Add("LyrP_Loading", ("Looking for the lyrics…", "Đang tìm lời bài hát…"));
        d.Add("LyrP_Instrumental", ("No words in this one, just enjoy the music.", "Bài này không lời, nghe nhạc thôi."));
        d.Add("LyrP_Error", ("Couldn't reach LRCLIB. Check the connection or the proxy in Settings.", "Không kết nối được LRCLIB. Kiểm tra mạng hoặc proxy trong Cài đặt."));
        d.Add("LyrP_Off", ("Lyrics are turned off in Settings.", "Lời bài hát đang tắt trong Cài đặt."));
        d.Add("LyrP_NotFound", ("No synced lyrics found for this song.", "Không tìm thấy lyrics cho bài này."));
        d.Add("LyrP_SearchOthers", ("Find other lyrics", "Tìm lyrics khác"));
        d.Add("LyrP_Back", ("Back", "Quay lại"));
        d.Add("LyrP_Find", ("Search", "Tìm"));
        d.Add("LyrP_SearchHint", ("Wrong song, or nothing found? Shorten the name and search again.", "Sai bài hoặc không thấy? Rút gọn tên bài rồi tìm lại."));
        d.Add("LyrP_Searching", ("Searching LRCLIB…", "Đang tìm trên LRCLIB…"));
        d.Add("LyrP_SearchFailed", ("The search didn't go through. Try again in a moment.", "Tìm không được, thử lại sau chút nhé."));
        d.Add("LyrP_NoResults", ("Nothing found. Try just the song's name.", "Không thấy gì. Thử chỉ gõ tên bài."));
        d.Add("LyrP_PickOne", ("Pick the right one (✓ = same length as what's playing):", "Chọn bài đúng (✓ = dài bằng bài đang phát):"));
        d.Add("LyrP_Synced", ("Synced", "Chạy theo nhạc"));
        d.Add("LyrP_WordsOnly", ("Words only", "Chỉ có lời"));
        d.Add("LyrP_NoWords", ("No words", "Không lời"));
        d.Add("LyrP_Earlier", ("Lyrics earlier (0.25 s)", "Lyrics sớm hơn (0,25 giây)"));
        d.Add("LyrP_Later", ("Lyrics later (0.25 s)", "Lyrics muộn hơn (0,25 giây)"));
        d.Add("LyrP_OffsetTip", ("Click to reset. The scroll wheel over the lyrics on the taskbar moves them too.", "Bấm để đặt lại. Lăn chuột trên lyrics ở taskbar cũng chỉnh được."));
        d.Add("LyrP_InTime", ("On time", "Đúng nhịp"));
        d.Add("LyrP_Offset", ("Moved {0} s", "Lệch {0} giây"));
        d.Add("LyrP_Romanize", ("Romanization", "Phiên âm"));

        d.Add("Opt_Roman_Off", ("Off", "Tắt"));
        d.Add("Opt_Roman_Below", ("Under each line", "Dưới mỗi dòng"));
        d.Add("Opt_Roman_Replace", ("Instead of the line", "Thay cho lời gốc"));
    }
}

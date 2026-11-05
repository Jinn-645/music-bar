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
        d.Add("LyrP_OffsetTip", ("Click to reset. Hold a button to keep moving. The scroll wheel over the lyrics on the taskbar moves them too.", "Bấm để đặt lại. Nhấn giữ nút để chỉnh liên tục. Lăn chuột trên lyrics ở taskbar cũng chỉnh được."));
        d.Add("LyrP_InTime", ("On time", "Đúng nhịp"));
        // Plus is later, as the delay of subtitles in a video player.
        d.Add("LyrP_Late", ("Lyrics later +{0} s", "Lời trễ +{0} giây"));
        d.Add("LyrP_Early", ("Lyrics earlier −{0} s", "Lời sớm −{0} giây"));
        d.Add("LyrP_Romanize", ("Romanization", "Phiên âm"));

        d.Add("LyrP_From", ("{0} · lyrics from {1}", "{0} · lời từ {1}"));
        d.Add("LyrP_FromFile", ("your file", "file của bạn"));
        d.Add("LyrP_NetEaseNone", ("NetEase has no synced lyrics for that one. Try another result.", "NetEase không có lời chạy theo nhạc cho bài này. Thử kết quả khác."));
        d.Add("LyrSt_NetEase", ("downloaded from NetEase", "tải từ NetEase"));

        // ---- sharing on LRCLIB
        d.Add("LyrS_Link", ("Share on LRCLIB", "Gửi lên LRCLIB"));
        d.Add("LyrS_Title", ("Share these lyrics on LRCLIB", "Gửi lời này lên LRCLIB"));
        d.Add("LyrS_Intro", ("LRCLIB is the open lyrics library MusicBar uses. Sending your lyrics (or your corrected timing) lets everyone get them. They are public, so only send the right lyrics for the right song.",
            "LRCLIB là kho lời mở MusicBar đang dùng. Gửi lời của bạn (hoặc bản đã chỉnh giờ) lên là ai cũng có. Lời sẽ công khai, nên chỉ gửi lời đúng bài, đúng thời gian."));
        d.Add("LyrS_Song", ("Song", "Tên bài"));
        d.Add("LyrS_Artist", ("Artist", "Ca sĩ"));
        d.Add("LyrS_Album", ("Album (leave empty if unsure)", "Album (không rõ thì để trống)"));
        d.Add("LyrS_Facts", ("{0} timed lines · length {1}", "{0} dòng có thời gian · dài {1}"));
        d.Add("LyrS_FactsOffset", ("{0} timed lines · length {1} · your correction ({2}) is folded into the times", "{0} dòng có thời gian · dài {1} · phần bạn chỉnh ({2}) đã được tính vào thời gian"));
        d.Add("LyrS_Send", ("Send", "Gửi"));
        d.Add("LyrS_Stage_challenge", ("Asking LRCLIB…", "Đang hỏi LRCLIB…"));
        d.Add("LyrS_Stage_solving", ("Solving LRCLIB's anti-spam puzzle (a few seconds)…", "Đang giải bài toán chống spam của LRCLIB (vài giây)…"));
        d.Add("LyrS_Stage_sending", ("Sending…", "Đang gửi…"));
        d.Add("LyrS_Done", ("Sent, thank you! LRCLIB may take up to a day to show the new version everywhere.", "Gửi xong, cảm ơn bạn! LRCLIB có thể mất tới 1 ngày để bản mới hiện ở mọi nơi."));
        d.Add("LyrS_Failed", ("LRCLIB didn't take it. Check the connection and try again later.", "LRCLIB chưa nhận. Kiểm tra mạng rồi thử lại sau nhé."));
        d.Add("LyrS_Refused", ("LRCLIB refused it: {0}", "LRCLIB không nhận: {0}"));
        d.Add("LyrS_NeedName", ("Fill in the song and the artist.", "Điền tên bài và ca sĩ."));
        d.Add("LyrS_NeedLength", ("The song's length isn't known; play it in a player that reports it.", "Chưa biết độ dài bài; phát bài bằng trình phát có báo độ dài."));
        d.Add("LyrS_TooFew", ("Too few timed lines to be the whole song.", "Ít dòng có thời gian quá, chưa giống cả bài."));
        d.Add("LyrS_TooLong", ("Some lines start after the song ends: the lyrics don't fit this recording.", "Có dòng bắt đầu sau khi bài đã hết: lời không khớp bản này."));

        d.Add("Opt_Roman_Off", ("Off", "Tắt"));
        d.Add("Opt_Roman_Below", ("Under each line", "Dưới mỗi dòng"));
        d.Add("Opt_Roman_Replace", ("Instead of the line", "Thay cho lời gốc"));
    }
}

using System.Collections.Generic;

namespace MusicBar.Infrastructure;

public static partial class Strings
{
    /// <summary>The face's menu, outfit, focus timer and what it says.</summary>
    static partial void AddFace(Dictionary<string, (string En, string Vi)> d)
    {
        d.Add("Face_Menu_Style", ("Face", "Kiểu mặt"));
        d.Add("Face_Menu_Wear", ("Hats and glasses", "Mũ, kính, phụ kiện"));

        d.Add("Opt_Hat_None", ("No hat", "Không đội mũ"));
        d.Add("Opt_Hat_Cap", ("Cap", "Mũ lưỡi trai"));
        d.Add("Opt_Hat_Beanie", ("Beanie", "Mũ len"));
        d.Add("Opt_Hat_Crown", ("Crown", "Vương miện"));
        d.Add("Opt_Hat_Santa", ("Santa hat", "Mũ Noel"));
        d.Add("Opt_Hat_Witch", ("Witch hat", "Mũ phù thủy"));
        d.Add("Opt_Hat_Bunny", ("Rabbit ears", "Tai thỏ"));
        d.Add("Opt_Hat_Mai", ("Apricot blossom", "Cành hoa mai"));
        d.Add("Opt_Glasses_None", ("No glasses", "Không đeo kính"));
        d.Add("Opt_Glasses_Round", ("Round glasses", "Kính tròn"));
        d.Add("Opt_Glasses_Shades", ("Sunglasses", "Kính râm"));
        d.Add("Opt_Glasses_Pixel", ("Pixel shades", "Kính pixel \"deal with it\""));

        d.Add("Face_Focus_Start", ("Start focusing ({0} min)", "Bắt đầu tập trung ({0} phút)"));
        d.Add("Face_Focus_Left", ("Focusing: {0} left", "Đang tập trung: còn {0}"));
        d.Add("Face_Focus_BreakLeft", ("On a break: {0} left", "Đang nghỉ: còn {0}"));
        d.Add("Face_Focus_SkipToBreak", ("Take the break now", "Nghỉ luôn bây giờ"));
        d.Add("Face_Focus_SkipBreak", ("End the break", "Thôi nghỉ, làm tiếp"));
        d.Add("Face_Focus_Stop", ("Stop the focus timer", "Dừng hẹn giờ tập trung"));
        d.Add("Face_Focus_Go", ("{0} minutes of focus. Let's go!", "Tập trung {0} phút nào, mình canh giờ cho!"));
        d.Add("Face_Focus_Back", ("Break's over, back to it: {0} minutes.", "Hết giờ nghỉ rồi, quay lại làm {0} phút nữa nhé."));
        d.Add("Face_Focus_BreakStretch", ("Break time, {0} minutes. Stand up and stretch a bit!", "Nghỉ {0} phút thôi. Đứng dậy vươn vai cái nào!"));
        d.Add("Face_Focus_BreakWater", ("Break time, {0} minutes. Have a glass of water!", "Nghỉ {0} phút. Đi uống ngụm nước đi!"));
        d.Add("Face_Focus_LongBreak", ("Four rounds done! Take a long break: {0} minutes.", "Xong 4 hiệp rồi, nghỉ dài {0} phút đi!"));
    }
}

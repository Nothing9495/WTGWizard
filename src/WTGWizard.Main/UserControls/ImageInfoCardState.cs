namespace WTGWizard.UserControls;

/// <summary>
/// 映像信息卡片四态：未选择 / 加载中 / 已选择且正常 / 已选择但打开失败。
/// </summary>
public enum ImageInfoCardState
{
    NoImage,
    Loading,
    Normal,
    Error
}

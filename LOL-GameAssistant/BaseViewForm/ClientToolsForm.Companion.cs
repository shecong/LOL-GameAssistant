using Button = AntdUI.Button;
using LOL_GameAssistant.Bootstrap;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class ClientToolsForm
{
    private Control CreateAugmentReviewCard()
    {
        var card = Card("增幅识别校正", 110, out FlowLayoutPanel body);
        body.Controls.Add(Button("查看三张卡片与校正名称", (_, _) => { using var form = new AugmentReviewForm(AppCompositionRoot.AugmentScanner, AppCompositionRoot.AugmentReview); form.ShowDialog(FindForm()); }));
        return card;
    }
    private Control CreatePartyCard()
    {
        var card = Card("开黑大厅", 175, out FlowLayoutPanel body);
        body.Controls.Add(Caption("选择模式、邀请已有好友、设置分路，核对队伍状态后开始匹配。也可从休息室招募进入。", 590));
        body.Controls.Add(Button("打开开黑大厅", (_, _) => { using var form = new PartyHubForm(AppCompositionRoot.PartyService); form.ShowDialog(FindForm()); }));
        return card;
    }
}

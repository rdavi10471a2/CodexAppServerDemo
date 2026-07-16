namespace CodexAppServerBlazor.Components.Pages.Home.MergeReviewDialog;

public sealed record MergeReviewDecision(
    string StagedRecordId,
    bool IsAccept,
    bool ForceApproveValidation);

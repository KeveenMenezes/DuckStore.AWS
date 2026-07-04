namespace BuildingBlocks.Messaging.Streams;

// The before/after view of a single DynamoDB Streams record, already deserialized into a
// module-owned snapshot type. Carrying both images is what lets a rule detect a *transition*
// (e.g. Old.Status != "Approved" && New.Status == "Approved") — something a new-image-only
// view cannot express. Old is null on INSERT, New is null on REMOVE.
public sealed record StreamContext<TImage>(string EventName, TImage? Old, TImage? New);

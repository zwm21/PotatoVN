using System;

namespace GalgameManager.Models;

public class PvnException(string msg) : Exception(msg)
{
    public string FullMsg { get; protected set; } = msg;
}

/// <summary>
/// 扫描结果仅通过名称匹配到已有逻辑游戏，需要用户确认关联。
/// </summary>
/// <param name="candidateGameId">候选逻辑游戏Id</param>
/// <param name="candidateName">候选逻辑游戏名</param>
public sealed class NameOnlyGameMatchException(Guid candidateGameId, string candidateName)
    : PvnException($"A game with the same name already exists: {candidateName}")
{
    public Guid CandidateGameId { get; } = candidateGameId; // 候选逻辑游戏Id
    public string CandidateName { get; } = candidateName; // 候选逻辑游戏名
}

/// <summary>
/// 用户在确认/选择对话框中主动取消时抛出。
/// 与“解析失败”区分开，调用方不得静默吞掉后继续原操作。
/// </summary>
public sealed class PvnUserCanceledException(string message) : PvnException(message);

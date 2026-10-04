namespace GodotAls.Core.Actions;

public readonly record struct AlsMontageActionRequest(int ActionDefinitionId,float PlayRate,float StartTime=0,bool StopGroup=true);

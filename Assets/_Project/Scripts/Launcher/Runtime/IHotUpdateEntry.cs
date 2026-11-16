
namespace PrismTactics.Launcher
{
    /// <summary>
    /// AOT 与热更 DLL 之间唯一的稳定契约。
    /// 正式版可以在这里加技能/敌人/地图规则接口，但不要加 Unity 生命周期方法。
    /// </summary>
    public interface IHotUpdateEntry
    {
        string Version { get; }//热更版本号
        string BootMessage { get; }//开机提示信息。
        int StartingCostBonus { get; }//初始费用
        string SelfCheck();//自检方法，返回运行状态。
        void OnRegister();
    }
}

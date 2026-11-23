// ============================================================================
//  v1.7 新增校验规则的测试
// ============================================================================
//  【这组测试保护什么】
//  1. 建筑等级（兵营/市集）越界必须被抓——否则升级系统会写出 Lv9 的城；
//  2. 州 Id 必须已分组——否则 S1 尺度的地图分层会静默失效（全部落进同一州）；
//  3. 玩家班底必须有短板——否则「用有限的人凑合着用」变成无差别选择；
//  4. 智谋/魅力也必须在 20-95——新增的部门效率直接吃这两个属性，
//     越界会让效率算出 200% 以上，经济直接失衡。
//  全部走真实文件链路（复制正式数据 → 改一个字段 → 加载 → 校验），
//  不在内存里 new 对象，避免「只在内存里生效、真实 JSON 走不到」的假绿。
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using StrategyGame.Data.Config;
using Xunit;

namespace StrategyGame.Data.Tests;

public class DataV17Tests
{
    private static string PatchCities(Func<JsonObject, bool> mutate)
        => Patch("cities.json", mutate);

    private static string PatchOfficers(Func<JsonObject, bool> mutate)
        => Patch("officers.json", mutate);

    private static string Patch(string fileName, Func<JsonObject, bool> mutate)
    {
        var path = Path.Combine(TestPaths.GoodDataDir, fileName);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.True(mutate(root), "测试自身写错：没有命中要修改的节点。");

        return Loader.CopyAndOverride(fileName, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ValidationResult ValidateDir(string dir) => ConfigValidator.Validate(new JsonConfigLoader(dir).LoadAll());

    [Fact(DisplayName = "兵营等级越界（9）必须被抓")]
    public void BarracksLevel_OutOfRange_IsRejected()
    {
        var dir = PatchCities(root =>
        {
            var states = root["cityStates"]!.AsArray();
            if (states.Count == 0) return false;
            states[0]!["barracksLevel"] = 9;
            return true;
        });

        var result = ValidateDir(dir);

        Assert.Contains(result.Errors, e =>
            e.Code == ValidationCode.ValueOutOfRange && e.Message.Contains("兵营等级"));
    }

    [Fact(DisplayName = "市集等级为负必须被抓")]
    public void MarketLevel_Negative_IsRejected()
    {
        var dir = PatchCities(root =>
        {
            var states = root["cityStates"]!.AsArray();
            if (states.Count == 0) return false;
            states[0]!["marketLevel"] = -1;
            return true;
        });

        var result = ValidateDir(dir);

        Assert.Contains(result.Errors, e =>
            e.Code == ValidationCode.ValueOutOfRange && e.Message.Contains("市集等级"));
    }

    [Fact(DisplayName = "州 Id 为 -1 必须被抓")]
    public void ProvinceId_Unassigned_IsRejected()
    {
        var dir = PatchCities(root =>
        {
            var states = root["cityStates"]!.AsArray();
            if (states.Count == 0) return false;
            states[0]!["provinceId"] = -1;
            return true;
        });

        var result = ValidateDir(dir);

        Assert.Contains(result.Errors, e =>
            e.Code == ValidationCode.ValueOutOfRange && e.Message.Contains("州 Id"));
    }

    [Fact(DisplayName = "玩家班底全员四维均衡（无短板）必须被抓")]
    public void PlayerOfficers_WithoutFlaw_IsRejected()
    {
        var dir = PatchOfficers(root =>
        {
            var states = root["officerStates"]!.AsArray();
            var playerIds = states
                .Where(s => (int)s!["ownerFactionId"]! == ConfigValidator.PlayerFactionId)
                .Select(s => (int)s!["officerId"]!)
                .ToHashSet();

            if (playerIds.Count == 0) return false;

            foreach (var o in root["officers"]!.AsArray())
            {
                if (!playerIds.Contains((int)o!["id"]!)) continue;
                o["command"] = 50;
                o["governance"] = 50;
                o["intellect"] = 50;
                o["charisma"] = 50;
            }

            return true;
        });

        var result = ValidateDir(dir);

        Assert.Contains(result.Errors, e =>
            e.Code == ValidationCode.OfficerAttrRange && e.Message.Contains("缺少明显短板"));
    }

    [Fact(DisplayName = "智谋越界（120）必须被抓")]
    public void Intellect_OutOfRange_IsRejected()
    {
        var dir = PatchOfficers(root =>
        {
            var officers = root["officers"]!.AsArray();
            if (officers.Count == 0) return false;
            officers[0]!["intellect"] = 120;
            return true;
        });

        var result = ValidateDir(dir);

        Assert.Contains(result.Errors, e =>
            e.Code == ValidationCode.OfficerAttrRange && e.Message.Contains("智谋"));
    }


    [Fact(DisplayName = "城池 Id 必须 1-7 连续")]
    public void CityIds_AreContiguous()
    {
        var data = Loader.Good().LoadAll();
        var ids = data.Cities.Select(c => c.Id).OrderBy(x => x).ToList();

        Assert.Equal(Enumerable.Range(1, ids.Count), ids);
    }

    [Fact(DisplayName = "每个势力恰好一座首都，且首都属于自己")]
    public void EachFaction_HasExactlyOneCapital()
    {
        var data = Loader.Good().LoadAll();

        foreach (var f in data.Factions)
        {
            var capitals = data.Cities.Where(c => c.FactionId == f.Id && c.IsCapital).ToList();
            Assert.Single(capitals);
            Assert.Equal(f.CapitalCityId, capitals[0].Id);
        }
    }

    [Fact(DisplayName = "玩家恰好两城且相邻")]
    public void Player_HasTwoAdjacentCities()
    {
        var data = Loader.Good().LoadAll();
        var player = data.Cities.Where(c => c.FactionId == ConfigValidator.PlayerFactionId)
            .OrderBy(c => c.Id).ToList();

        Assert.Equal(data.Extended.ExpectedPlayerCityCount, player.Count);
        Assert.Contains(player[1].Id, player[0].AdjacentCityIds);
        Assert.Contains(player[0].Id, player[1].AdjacentCityIds);
    }

    [Fact(DisplayName = "所有人才四维都在 20-95")]
    public void AllOfficerAttributes_InRange()
    {
        var data = Loader.Good().LoadAll();

        foreach (var o in data.Officers)
        {
            Assert.InRange(o.Command, 20, 95);
            Assert.InRange(o.Governance, 20, 95);
            Assert.InRange(o.Intellect, 20, 95);
            Assert.InRange(o.Charisma, 20, 95);
        }
    }

    [Fact(DisplayName = "开局建筑等级为 0、州 Id 均已分组")]
    public void ShippedCityStates_AreInitialized()
    {
        var data = Loader.Good().LoadAll();

        foreach (var s in data.CityStates)
        {
            Assert.Equal(0, s.BarracksLevel);
            Assert.Equal(0, s.MarketLevel);
            Assert.True(s.ProvinceId >= 0, $"城池 {s.CityId} 的州 Id 未分组。");
        }

        // 三个势力必须落在三个不同的州，否则 S1 尺度看不出「州」的边界
        Assert.Equal(3, data.CityStates.Select(s => s.ProvinceId).Distinct().Count());
    }

    [Fact(DisplayName = "正式数据必须零错误零短板告警")]
    public void ShippedData_IsClean()
    {
        var (_, result) = Loader.GoodValidated();

        Assert.False(result.HasErrors, result.ToReport());
    }
}

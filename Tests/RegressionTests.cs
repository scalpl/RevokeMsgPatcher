using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using RevokeMsgPatcher;
using RevokeMsgPatcher.Model;
using RevokeMsgPatcher.Matcher;
using RevokeMsgPatcher.Utils;

class RegressionTests
{
    static int assertions;
    static string fixture;
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); assertions++; Console.WriteLine("PASS " + message); }
    static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    static void ExpectError(string code, Action action) { try { action(); } catch (BusinessException e) { Assert(e.ErrorCode == code, code); return; } throw new Exception("Expected " + code); }
    static ReplacePattern Rule(byte from, byte to, string category, int expected = 0) { return new ReplacePattern { Search = new[] { from }, Replace = new[] { to }, Category = category, ExpectedMatches = expected }; }
    static void Synthetic()
    {
        var a = Rule(1, 2, "A"); var b = Rule(3, 4, "B");
        File.WriteAllBytes(fixture, new byte[] { 1, 1 });
        Assert(ModifyFinder.FindChanges(fixture, new List<ReplacePattern> { a }).Count == 2, "legacy multiple locations retained");
        ExpectError("match_inconformity", () => ModifyFinder.FindChanges(fixture, new List<ReplacePattern> { a, b }));
        a.ExpectedMatches = 1;
        ExpectError("match_inconformity", () => ModifyFinder.FindChanges(fixture, new List<ReplacePattern> { a }));
        File.WriteAllBytes(fixture, new byte[] { 2, 3 });
        Assert(ModifyFinder.FindChanges(fixture, new List<ReplacePattern> { a, b }).Count == 1, "mixed installed and uninstalled categories");
        b.Category = "A";
        Assert(!ModifyFinder.FindReplacedFunction(fixture, new List<ReplacePattern> { a, b }).Contains("A"), "incomplete category is not installed");
        File.WriteAllBytes(fixture, new byte[] { 2, 4 });
        Assert(ModifyFinder.FindReplacedFunction(fixture, new List<ReplacePattern> { a, b }).Contains("A"), "complete category is installed");
        ExpectError("match_already_replace", () => ModifyFinder.FindChanges(fixture, new List<ReplacePattern> { a, b }));
        ExpectError("match_no_pattern", () => ModifyFinder.FindChanges(fixture, new List<ReplacePattern>()));
        ExpectError("match_no_pattern", () => ModifyFinder.FindChanges(fixture, null));
        a.Search = new byte[0]; a.Replace = new byte[0];
        ExpectError("match_inconformity", () => ModifyFinder.FindChanges(fixture, new List<ReplacePattern> { a }));
    }
    static void Binary(string path, List<ReplacePattern> rules)
    {
        byte[] original = File.ReadAllBytes(path);
        Assert(Hash(original) == "3a65d5f38991e32129afa83bc1c92b29babf316199b67c7a47614a9100fb03ca", "verified original Weixin 4.1.15.12 x64");
        var multi = rules.Where(p => p.Category == "多开").ToList();
        for (int state = 0; state < 8; state++)
        {
            byte[] input = (byte[])original.Clone();
            if ((state & 1) != 0) input[0x229a79e] = 0x29;
            if ((state & 2) != 0) { input[0x48cb20d] = 0x90; input[0x48cb20e] = 0xe9; }
            if ((state & 4) != 0) { input[0x90ab6] = 0x90; input[0x90ab7] = 0xe9; }
            byte[] expected = (byte[])input.Clone();
            expected[0x90ab6] = 0x90; expected[0x90ab7] = 0xe9; expected[0x48cb20d] = 0x0f; expected[0x48cb20e] = 0x85;
            File.WriteAllBytes(fixture, input);
            if (input.SequenceEqual(expected)) ExpectError("match_already_replace", () => ModifyFinder.FindChanges(fixture, multi));
            else FileUtil.EditMultiHex(fixture, ModifyFinder.FindChanges(fixture, multi));
            Assert(File.ReadAllBytes(fixture).SequenceEqual(expected), "state " + state + ": exact output and anti-recall preserved");
            Assert(ModifyFinder.FindReplacedFunction(fixture, multi).Contains("多开"), "state " + state + ": installed detection");
            ExpectError("match_already_replace", () => ModifyFinder.FindChanges(fixture, multi));
        }
        File.WriteAllBytes(fixture, original);
        FileUtil.EditMultiHex(fixture, ModifyFinder.FindChanges(fixture, rules));
        original[0x229a79e] = 0x29; original[0x90ab6] = 0x90; original[0x90ab7] = 0xe9;
        Assert(File.ReadAllBytes(fixture).SequenceEqual(original), "combined anti-recall and multi-instance patch");
    }
    static int Main(string[] args)
    {
        fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            Synthetic();
            var serializer = new JavaScriptSerializer();
            var external = serializer.Deserialize<Bag>(File.ReadAllText(Path.Combine(args[0], "RevokeMsgPatcher.Assistant", "Data", "2.1", "patch.json")));
            var xml = XDocument.Load(Path.Combine(args[0], "RevokeMsgPatcher", "Properties", "Resources.resx"));
            var embedded = serializer.Deserialize<Bag>(xml.Root.Elements("data").Single(e => (string)e.Attribute("name") == "PatchJson").Element("value").Value);
            var entry = external.Apps["Weixin"].FileCommonModifyInfos["Weixin.dll"][0];
            var embeddedEntry = embedded.Apps["Weixin"].FileCommonModifyInfos["Weixin.dll"][0];
            Assert(entry.Name == embeddedEntry.Name && entry.StartVersion == embeddedEntry.StartVersion && entry.EndVersion == embeddedEntry.EndVersion && entry.ReplacePatterns.Count == embeddedEntry.ReplacePatterns.Count && entry.ReplacePatterns.Zip(embeddedEntry.ReplacePatterns, (a, b) => a.Search.SequenceEqual(b.Search) && a.Replace.SequenceEqual(b.Replace) && a.Category == b.Category && a.ExpectedMatches == b.ExpectedMatches && a.Tips == b.Tips).All(equal => equal), "embedded and external new rules agree");
            Assert(entry.StartVersion == "4.1.15.11" && entry.EndVersion == "4.1.15.12", "version scope bounded to verified build");
            Assert(entry.ReplacePatterns.All(p => p.ExpectedMatches == 1), "new rules require unique matches");
            if (args.Length > 1 && !string.IsNullOrEmpty(args[1])) Binary(args[1], entry.ReplacePatterns);
            else Console.WriteLine("SKIP binary tests: pass -OriginalDll with an unmodified 4.1.15.12 DLL.");
            Console.WriteLine("ALL TESTS PASSED: " + assertions); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1; }
        finally { if (File.Exists(fixture)) File.Delete(fixture); }
    }
}

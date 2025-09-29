using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using Veldrid.ImageSharp;
using FuzzySharp;
using System.IO.Pipes;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TinyDialogsNet;

namespace yahd2mm;

struct HDMModGroup {
  public bool IsGrouped;
  public HD2Mod[] mods;
  public string GroupName;
  public string ModId;
}

partial class EntryPoint
{
  public static void RunMain()
  {
    if (!Directory.Exists(ModManager.yahd2mm_basepath))
    {
      Directory.CreateDirectory(ModManager.yahd2mm_basepath);
    }
    Environment.CurrentDirectory = AppContext.BaseDirectory;
    ScanForHD2Path();
    if (HD2Path == string.Empty) {
      NeedsHD2DataPath = !(File.Exists(HD2PathFile) && IsValidHD2Directory(File.ReadAllText(HD2PathFile).Trim()));
    }
    else {
      NeedsHD2DataPath = false;
    }
    Configuration.Default.PreferContiguousImageBuffers = true;
    bool existingClientExists = false;
    try
    {
      using NamedPipeClientStream client = new(".", "yahd2mm.pipe", PipeDirection.Out);
      client.Connect(2);
      using StreamWriter writer = new(client, Encoding.UTF8);
      writer.WriteLine("ExistingInstanceCheck");
      existingClientExists = true;
    }
    catch (TimeoutException) { }
    if (existingClientExists)
    {
      return;
    }
    files = new();
    queue = OperatingSystem.IsLinux() ? new LinuxFilesystemQueue() : new WindowsFilesystemQueue();
    queue.StartThread();
    VeldridStartup.CreateWindowAndGraphicsDevice(
      new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "yahd2mm"),
      new GraphicsDeviceOptions(true, null, true, ResourceBindingModel.Improved, true, true),
      out window,
      out gd);
    cl = gd.ResourceFactory.CreateCommandList();
    controller = new(
      gd,
      gd.MainSwapchain.Framebuffer.OutputDescription,
      window.Width,
      window.Height);
    window.Resized += () =>
    {
      gd.MainSwapchain.Resize((uint)window.Width, (uint)window.Height);
      controller.WindowResized(window.Width, window.Height);
    };
    if (!NeedsKey && !NeedsHD2DataPath && IsAdministrator())
    {
      StartManager();
      UseHardlinks = !OperatingSystem.IsLinux() && Path.GetPathRoot(File.ReadAllText(Path.Join(ModManager.yahd2mm_basepath, "path.txt")).Trim()) == Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
    Stopwatch stopwatch = Stopwatch.StartNew();
    float deltaTime = 0f;
    Vector3 clearColor = new(0f, 0f, 0f);
    while (window.Exists)
    {
      deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
      stopwatch.Restart();
      InputSnapshot snapshot = window.PumpEvents();
      if (!window.Exists) { break; }
      controller.Update(deltaTime, snapshot);
      DoUI();
      //ImGui.ShowDemoWindow();
      cl.Begin();
      cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
      cl.ClearColorTarget(0, new RgbaFloat(clearColor.X, clearColor.Y, clearColor.Z, 1f));
      controller.Render(gd, cl);
      cl.End();
      gd.SubmitCommands(cl);
      gd.SwapBuffers(gd.MainSwapchain);
    }
  }

  internal static bool SwitchToDownloads = false;
  
  private static void DoUI()
  {
    ImGui.SetNextWindowSize(new Vector2(window.Width, window.Height));
    ImGui.SetNextWindowPos(new Vector2(0));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
    ImGui.Begin("yahd2mm", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
    if (!NeedsKey && !NeedsHD2DataPath && IsAdministrator())
    {
      ImGui.BeginTabBar("Selection");
      if (ImGui.BeginTabItem("Mod List"))
      {
        DoModList();
        ImGui.EndTabItem();
      }
      if (ImGui.BeginTabItem("Priorities")) {
        DoPriorities();
        ImGui.EndTabItem();
      }
      bool p_open = true;
      // wack little workaround because imgui.net (and assumedly by extension imgui) doesnt let you forcibly select something
      if (SwitchToDownloads)
      {
        if (ImGui.BeginTabItem("Downloads", ref p_open, ImGuiTabItemFlags.SetSelected))
        {
          SwitchToDownloads = false;
          DoDownloads();
          ImGui.EndTabItem();
        }
      }
      else {
        if (ImGui.BeginTabItem("Downloads"))
        {
          DoDownloads();
          ImGui.EndTabItem();
        }
      }
      if (ImGui.BeginTabItem("Completed Downloads"))
      {
        DoCompletedDownloads();
        ImGui.EndTabItem();
      }
      if (ImGui.BeginTabItem("Modpacks"))
      {
        DoModpacks();
        ImGui.EndTabItem();
      }
      if (ImGui.BeginTabItem("Local Files")) {
        DoLocalFiles();
        ImGui.EndTabItem();
      }
      if (ImGui.BeginTabItem("Settings")) {
        DoSettings();
        ImGui.EndTabItem();
      }
      ImGui.EndTabBar();
    }
    if (NeedsHD2DataPath)
    {
      ImGui.OpenPopup("Data Path");
    }
    PromptForDataPath();
    if (!NeedsHD2DataPath && NeedsKey)
    {
      ImGui.OpenPopup("Key Prompt");
    }
    PromptForKey();
    if (!NeedsHD2DataPath && !NeedsKey && !IsAdministrator()) {
      ImGui.OpenPopup("Admin Permissions");
    }
    PromptForAdmin();
    ImGui.End();
    ImGui.PopStyleVar(1);
  }

  private static void PromptForAdmin()
  {
    ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    if (ImGui.BeginPopupModal("Admin Permissions", ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.TextUnformatted("Your Helldivers 2 install is on a different drive than where yahd2mm downloads mods to.");
      ImGui.TextUnformatted("Hardlinks do not function across drive boundaries, and as such symlinks are required.");
      ImGui.TextUnformatted("Symlinks require admin permissions. yahd2mm will relaunch with admin permissions.");
      if (ImGui.Button("I understand.")) {
        manager?.server?.Dispose();
        ProcessStartInfo info = new()
        {
          FileName = Environment.ProcessPath,
          UseShellExecute = true,
          Verb = "runas"
        };
        System.Diagnostics.Process.Start(info);
        Environment.Exit(0);
      }
      ImGui.EndPopup();
    }
  }

  private static readonly ConfigData data = Config.cfg;

  private static void DoSettings() {
    if (ImGui.Checkbox("Activate newly installed mods", ref data.ActivateOnInstall)) {
      Config.SaveConfig();
    }
    if (ImGui.Checkbox("Activate all options on newly installed mods", ref data.ActivateOptionsOnInstall)) {
      Config.SaveConfig();
    }
    if (ImGui.Checkbox("Open downloads tab when starting new download", ref data.OpenDownloadsOnNew)) {
      Config.SaveConfig();
    }
  }

  private static void DoCompletedDownloads() {
    if (ImGui.Button("Clear")) {
      manager.downloadManager.progresses = new(manager.downloadManager.progresses.ToDictionary().Where((v) => v.Value.status != DownloadStatus.Done));
      manager.modNames.Clear();
    }
    ImGui.BeginChild("ScrollableDownloads", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    foreach (KeyValuePair<string, DownloadState> progress in manager.downloadManager.progresses.ToList().Where((v) => v.Value.status == DownloadStatus.Done || v.Value.status == DownloadStatus.Cancelled))
    {
      DoCompletedDownload(progress);
    }
    ImGui.EndChild();
  }

  private static void DoCompletedDownload(KeyValuePair<string, DownloadState> kvp) {
    if (manager.modNames.TryGetValue(kvp.Key, out string modName))
    {
      float width = ImGui.GetContentRegionAvail().X;
      DownloadState progress = kvp.Value;
      ImGui.BeginChild("download" + Path.GetFileName(progress.outputPath), new Vector2(width, 100), ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AutoResizeX);
      ImGui.TextUnformatted($"Filename: {Path.GetFileName(progress.outputPath)}");
      ImGui.TextUnformatted($"Mod name: {modName}");
      ImGui.TextUnformatted($"Status: {(progress.status == DownloadStatus.Done ? "Done" : "Cancelled")}");
      ImGui.TextUnformatted($"Mod size: {FormatBytes(progress.totalBytes)}");
      ImGui.EndChild();
    }
  }

  private static int draggedIndex = -1;

  private static void DoPriorities() {
    if (ImGui.Button("Re-deploy with priority list")) {
      manager.modManager.ApplyPriorities();
    }
    ImGui.BeginChild("PriorityListChild", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    bool dropHandled = false;
    for (int i = 0; i < manager.modManager.priorities.Length; i++)
    {
      string basename = manager.modManager.priorities[i];
      string item = manager.modManager.modAliases[basename];
      ImGui.Selectable($"{(manager.modManager.modState[basename].Enabled ? "(X) " : "( ) ")}{i}: {item}###{basename}", false);

      if (ImGui.BeginDragDropSource())
      {
        draggedIndex = i;
        ImGui.SetDragDropPayload("MOD_PRIORITY_ITEM", IntPtr.Zero, 0);
        ImGui.TextUnformatted("Dragging: " + item);
        ImGui.EndDragDropSource();
      }

      if (ImGui.BeginDragDropTarget())
      {
        bool isValid;
        unsafe
        {
          isValid = ImGui.AcceptDragDropPayload("MOD_PRIORITY_ITEM").NativePtr != null;
        }
        if (isValid)
        {
          if (draggedIndex != -1 && draggedIndex != i)
          {
            var priorities = manager.modManager.priorities.ToList();
            string mod = priorities[draggedIndex];
            priorities.RemoveAt(draggedIndex);
            priorities.Insert(i, mod);
            manager.modManager.priorities = [.. priorities];
            draggedIndex = -1;
            manager.modManager.SaveData();
            dropHandled = true;
          }
        }
        ImGui.EndDragDropTarget();
      }
    }
    if (draggedIndex != -1)
    {
      ImGui.Dummy(ImGui.GetContentRegionAvail());
      if (!dropHandled && ImGui.BeginDragDropTarget())
      {
        bool isValid;
        unsafe
        {
          isValid = ImGui.AcceptDragDropPayload("MOD_PRIORITY_ITEM").NativePtr != null;
        }
        if (isValid && draggedIndex != -1)
        {
          List<string> priorities = [.. manager.modManager.priorities];
          string mod = priorities[draggedIndex];
          priorities.RemoveAt(draggedIndex);
          priorities.Add(mod);
          manager.modManager.priorities = [.. priorities];
          manager.modManager.SaveData();
          draggedIndex = -1;
        }
        ImGui.EndDragDropTarget();
      }
    }
    ImGui.EndChild();
  }

  private static void DoLocalFiles() {
    if (ImGui.Button("Open Folder")) {
      if (OperatingSystem.IsWindows()) {
        System.Diagnostics.Process.Start("explorer.exe", $"\"{LocalFileHolder.LocalFiles}\"");
      }
      else if (OperatingSystem.IsLinux()) {
        System.Diagnostics.Process.Start("xdg-open", $"\"{LocalFileHolder.LocalFiles}\"");
      }
    }
    ImGui.SameLine();
    if (ImGui.Button("Install All")) {
      foreach (string file in files.localFiles)
        manager.InstallFile(file);
    }
    ImGui.SameLine();
    if (ImGui.Button("Delete All")) {
      foreach (string file in files.localFiles)
        queue.Delete(file);
    }
    ImGui.SameLine();
    if (ImGui.Button("Select files not in folder")) {
      Task.Run(() =>
      {
        FileFilter filter = new("Archive files", ["*.7z", "*.rar", "*.zip"]);
        (bool cancelled, IEnumerable<string> paths) = TinyDialogs.OpenFileDialog("Open files", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), true, filter);
        if (!cancelled) {
          foreach (string file in paths) {
            manager.InstallFile(file);
          }
        }
      });
    }
    ImGui.BeginChild("ScrollableLocalFiles", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    foreach (string file in files.localFiles) {
      ImGui.BeginChild($"localFile{file}", new(ImGui.GetContentRegionAvail().X, 120), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AlwaysAutoResize);
      ImGui.BeginChild($"localFile{file}Name", new(ImGui.GetContentRegionAvail().X, 60), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysHorizontalScrollbar);
      ImGui.TextUnformatted(Path.GetFileName(file));
      ImGui.EndChild();
      ImGui.Spacing();
      if (ImGui.Button($"Install##{file}")) {
        manager.InstallFile(file);
      }
      ImGui.SameLine();
      ImGui.Spacing();
      ImGui.SameLine();
      if (ImGui.Button($"Delete##{file}")) {
        queue.Delete(file);
      }
      ImGui.EndChild();
    }
    ImGui.EndChild();
  }

  private static string SearchingString = string.Empty;

  private static void DoModList()
  {
    if (ImGui.Button("Enable all mods"))
    {
      foreach (HD2Mod mod in manager.modManager.mods.ToList())
      {
        manager.modManager.EnableMod(mod.Guid);
      }
      manager.modManager.CheckForPatchGaps();
    }
    ImGui.SameLine();
    if (ImGui.Button("Disable all mods"))
    {
      List<HD2Mod> reversed = [.. manager.modManager.mods];
      reversed.Reverse();
      foreach (HD2Mod mod in reversed)
      {
        manager.modManager.DisableMod(mod.Guid);
      }
      manager.modManager.CheckForPatchGaps();
    }
    ImGui.SameLine();
    if (ImGui.Button("Re-deploy"))
    {
      List<HD2Mod> enabled = [];
      foreach (HD2Mod mod in manager.modManager.mods.ToList())
      {
        if (manager.modManager.modState[mod.Guid].Enabled) {
          enabled.Add(mod);
        }
        try {
          manager.modManager.DisableMod(mod.Guid);
        }
        catch (Exception)
        {}
      }
      foreach (HD2Mod mod in enabled) {
        manager.modManager.EnableMod(mod.Guid);
      }
      queue.WaitForEmpty();
      manager.modManager.CheckForPatchGaps();
    }
    ImGui.SameLine();
    if (ImGui.Button("Purge mod files")) {
      foreach (string modFile in Directory.EnumerateFiles(HD2Path).Where((v) => ModManager.FileNumRegex.Match(v).Success)) {
        queue.Delete(modFile);
      }
      manager.modManager.fileRecords.Clear();
      ModManager.existing.Clear();
      manager.modManager.SaveData();
      queue.WaitForEmpty();
    }
    ImGui.InputText("Search", ref SearchingString, 80, ImGuiInputTextFlags.EscapeClearsAll | ImGuiInputTextFlags.AlwaysOverwrite);
    ImGui.TextUnformatted("Priority list is ignored when enabling/disabling mods here, apply in Priorities!");
    ImGui.BeginChild("ScrollableModlist", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    HD2Mod[] mods = [.. manager.modManager.mods];
    Array.Sort(mods, (x, y) => string.Compare(manager.modManager.modAliases[x.Guid], manager.modManager.modAliases[y.Guid]));
    mods = [.. mods.OrderByDescending((v) => manager.modManager.favourites.Contains(v.Guid))];
    if (SearchingString != "" && SearchingString != string.Empty)
    {
      static int GetRatio(HD2Mod mod) {
        KeyValuePair<string, NexusData> entry = manager.nexusIds.FirstOrDefault(kvp => kvp.Value.associatedGuids.Contains(mod.Guid));
        if (entry.Key != null)
        {
          NexusData data = entry.Value;
          if (data.associatedGuids.Length > 1)
          {
            if (data.modName.Contains(SearchingString, StringComparison.OrdinalIgnoreCase))
            {
              int nameRatio = Fuzz.Ratio(data.modName.ToLower(), SearchingString.ToLower());
              int modRatio = Fuzz.Ratio(mod.Name.ToLower(), SearchingString.ToLower());
              return Math.Max(nameRatio, modRatio);
            }
          }
        }
        return Fuzz.Ratio(mod.Name.ToLower(), SearchingString.ToLower());
      }
      mods = [.. mods
      .Where(mod =>
      {
        if (manager.modManager.modAliases[mod.Guid].Contains(SearchingString, StringComparison.OrdinalIgnoreCase))
          return true;
        if (manager.nexusReverse.TryGetValue(mod.Guid, out string? value)) {
          NexusData resultData = manager.nexusIds[value];
          return (resultData.associatedGuids.Length > 1 && resultData.modName.Contains(SearchingString, StringComparison.OrdinalIgnoreCase))
          || manager.modManager.modAliases[mod.Guid].Contains(SearchingString, StringComparison.OrdinalIgnoreCase);
        }
        return false;
      })
      .Select(mod => 
      {
        string alias = manager.modManager.modAliases[mod.Guid];
        int score = GetRatio(mod);
        bool isPrefix = alias.StartsWith(SearchingString, StringComparison.OrdinalIgnoreCase);
        bool contains = alias.Contains(SearchingString, StringComparison.OrdinalIgnoreCase);
        bool isFav = manager.modManager.favourites.Contains(mod.Guid);
        return new
        {
          Mod = mod,
          Score = score,
          IsPrefix = isPrefix,
          Contains = contains,
          IsFavourite = isFav
        };
      })
      .OrderByDescending(x => x.IsPrefix ? 2 : (x.Contains ? 1 : 0))
      .ThenByDescending(x => x.Score)
      .ThenBy(x => manager.modManager.modAliases[x.Mod.Guid].Length)
      .ThenByDescending(x => x.IsFavourite)
      .Select(x => x.Mod)];
    }
    List<HDMModGroup> grouped = [];
    List<string> encountered = [];
    foreach (HD2Mod mod in mods) {
      if (encountered.Contains(mod.Guid)) continue;
      if (manager.nexusReverse.TryGetValue(mod.Guid, out string? value))
      {
        NexusData data = manager.nexusIds[value];
        if (data.associatedGuids.Length > 1)
        {
          HD2Mod[] groupMods = [.. mods.Where(m => data.associatedGuids.Contains(m.Guid))];

          HDMModGroup group = new()
          {
            GroupName = data.modName,
            IsGrouped = true,
            mods = groupMods,
            ModId = value
          };
          encountered.AddRange(groupMods.Select(m => m.Guid));
          grouped.Add(group);
          continue;
        }
      }
      int lastNonGrouped = Array.IndexOf([.. grouped], Array.FindLast([.. grouped], (group) => !group.IsGrouped));
      if (lastNonGrouped == -1) {
        grouped.Add(new HDMModGroup() {
          mods = [mod],
          IsGrouped = false
        });
      }
      else
      {
        if (lastNonGrouped == grouped.Count - 1) {
          grouped[lastNonGrouped] = grouped[lastNonGrouped] with { mods = [.. grouped[lastNonGrouped].mods, mod] };
          encountered.Add(mod.Guid);
        }
        else
        {
          grouped.Add(new HDMModGroup() {
            mods = [mod],
            IsGrouped = false
          });
        }
      }
    }
    foreach (HDMModGroup group in grouped) {
      if (group.IsGrouped) {
        if (ImGui.CollapsingHeader($"{group.GroupName}###{group.ModId}")) {
          ImGui.Indent();
          if (ImGui.Button("Open mod on Nexus"))
          {
            OpenFile($"https://www.nexusmods.com/helldivers2/mods/{group.ModId}");
          }
          foreach (HD2Mod mod in group.mods) {
            DoMod(mod, false);
          }
          ImGui.Unindent();
        }
      }
      else {
        foreach (HD2Mod mod in group.mods) {
          DoMod(mod, true);
        }
      }
    }
    ImGui.EndChild();
  }

  private static void DoDownloads()
  {
    ImGui.BeginChild("ScrollableDownloads", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    foreach (KeyValuePair<string, DownloadState> progress in manager.downloadManager.progresses.ToList().Where((v) => v.Value.status != DownloadStatus.Done && v.Value.status != DownloadStatus.Cancelled))
    {
      DoDownload(progress);
    }
    ImGui.EndChild();
  }

  private static void DoModpacks()
  {
    ImGui.BeginChild("ScrollableModpacks", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    AddModpackButton();
    ModpackAdditionPopup();
    ModpackFromActivated();
    foreach (KeyValuePair<string, Modpack> packs in manager.modpackManager.modpacks)
    {
      if (ImGui.CollapsingHeader(packs.Value.Name))
      {
        ImGui.Indent();
        ImGui.PushID(packs.Key);
        ImGui.BeginGroup();
        if (ImGui.Button("Remove all mods"))
        {
          foreach (ModpackMod mod in packs.Value.mods)
          {
            manager.modpackManager.RemoveModFromModpack(mod.guid, packs.Key);
          }
        }
        if (ImGui.Button("Add active mods")) {
          foreach (HD2Mod amod in manager.modManager.mods.ToList())
          {
            if (manager.modManager.modState.TryGetValue(amod.Guid, out ModJson json))
              if (json.Enabled)
              {
                bool modHasChoices = manager.modManager.processedChoices.TryGetValue(amod.Guid, out ManifestChoices[]? choices);
                manager.modpackManager.AddModToModpack(amod.Name, amod.Guid, packs.Key, modHasChoices ? ModManager.ChoicesToPaths(choices!) : null);
              }
          }
        }
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        if (ImGui.Button("Load modpack"))
        {
          manager.modpackManager.LoadModpack(packs.Key, manager.modManager);
        }
        if (ImGui.Button("Delete modpack"))
        {
          manager.modpackManager.DeleteModpack(packs.Key);
        }
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        if (ImGui.Button("Overwrite with active mods")) {
          foreach (ModpackMod mod in packs.Value.mods)
          {
            manager.modpackManager.RemoveModFromModpack(mod.guid, packs.Key);
          }
          foreach (HD2Mod amod in manager.modManager.mods.ToList())
          {
            if (manager.modManager.modState.TryGetValue(amod.Guid, out ModJson json))
              if (json.Enabled)
              {
                bool modHasChoices = manager.modManager.processedChoices.TryGetValue(amod.Guid, out ManifestChoices[]? choices);
                manager.modpackManager.AddModToModpack(amod.Name, amod.Guid, packs.Key, modHasChoices ? ModManager.ChoicesToPaths(choices!) : null);
              }
          }
        }
        ImGui.EndGroup();
        if (ImGui.TreeNodeEx("Mods", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
          ImGui.Indent();
          foreach (ModpackMod mod in packs.Value.mods)
          {
            DoModpackMod(mod, packs.Key);
          }
          ImGui.Unindent();
          ImGui.TreePop();
        }
        ImGui.Unindent();
        ImGui.PopID();
      }
    }
    ImGui.EndChild();
  }
  private static void DoModpackMod(ModpackMod mod, string name)
  {
    ImGui.PushID(mod.guid);
    if (ImGui.TreeNodeEx(manager.modManager.mods.Any((v) => v.Guid == mod.guid) ? mod.name : $"(!!) {mod.name} (NOT INSTALLED)"))
    {
      if (mod.options != null)
      {
        ImGui.TextUnformatted("Enabled options:");
        ImGui.Indent();
        foreach (string opt in mod.options)
        {
          ImGui.TextUnformatted(opt);
        }
        ImGui.Unindent();
        ImGui.Spacing();
      }
      if (ImGui.Button("Remove mod"))
      {
        manager.modpackManager.RemoveModFromModpack(mod.guid, name);
      }
      ImGui.TreePop();
    }
    ImGui.PopID();
  }

  private static void ModpackFromActivated()
  {
    if (ImGui.Button("Create modpack from currently active mods"))
    {
      ImGui.OpenPopup("New Modpack");
    }
    if (ImGui.BeginPopupModal("New Modpack", ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.InputText("Modpack Name", ref ModpackName, 500);
      if (ImGui.Button("Confirm"))
      {
        string pack = manager.modpackManager.CreateModpack(ModpackName);
        ModpackName = string.Empty;
        foreach (HD2Mod mod in manager.modManager.mods.ToList())
        {
          if (manager.modManager.modState.TryGetValue(mod.Guid, out ModJson json))
            if (json.Enabled)
            {
              bool modHasChoices = manager.modManager.processedChoices.TryGetValue(mod.Guid, out ManifestChoices[]? choices);
              manager.modpackManager.AddModToModpack(mod.Name, mod.Guid, pack, modHasChoices ? ModManager.ChoicesToPaths(choices!) : null);
            }
        }
        ImGui.CloseCurrentPopup();
      }
      if (ImGui.Button("Cancel")) {
        ImGui.CloseCurrentPopup();
      }
      ImGui.EndPopup();
    }
  }

  private static string ModpackName = string.Empty;

  private static void ModpackAdditionPopup()
  {
    if (ImGui.BeginPopupModal("Add Modpack", ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.InputText("Modpack Name", ref ModpackName, 500);
      if (ImGui.Button("Confirm"))
      {
        manager.modpackManager.CreateModpack(ModpackName);
        ModpackName = string.Empty;
        ImGui.CloseCurrentPopup();
      }
      if (ImGui.Button("Cancel")) {
        ImGui.CloseCurrentPopup();
      }
      ImGui.EndPopup();
    }
  }

  private static void AddModpackButton()
  {
    if (ImGui.Button("Add Modpack"))
    {
      ImGui.OpenPopup("Add Modpack");
    }
  }

  private static string FormatBytes(long bytes)
  {
    string[] units = ["B", "KB", "MB", "GB"];
    double size = bytes;
    int unit = 0;
    while (size >= 1024 && unit < units.Length - 1)
    {
      size /= 1024;
      unit++;
    }
    return $"{size:0.###} {units[unit]}";
  }

  private static void DoDownload(KeyValuePair<string, DownloadState> progress)
  {
    float width = ImGui.GetContentRegionAvail().X;
    ImGui.BeginChild("download" + progress.Value.modName, new Vector2(width, 80), ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AutoResizeX);
    ImGui.TextUnformatted(progress.Value.modName);
    ImGui.ProgressBar(progress.Value.bytesRead / progress.Value.totalBytes, Vector2.Zero, $"{FormatBytes(progress.Value.bytesRead)}/{FormatBytes(progress.Value.totalBytes)}");
    if (progress.Value.status == DownloadStatus.Active) {
      if (ImGui.Button("Pause")) {
        manager.downloadManager.progresses[progress.Key] = manager.downloadManager.progresses[progress.Key] with { status = DownloadStatus.Paused };
      }
      ImGui.SameLine();
      if (ImGui.Button("Cancel")) {
        manager.downloadManager.progresses[progress.Key] = manager.downloadManager.progresses[progress.Key] with { status = DownloadStatus.Cancelled };
      }
    }
    else if (progress.Value.status == DownloadStatus.Paused) {
      if (ImGui.Button("Resume")) {
        manager.downloadManager.ResumeDownload(progress.Key);
      }
      ImGui.SameLine();
      if (ImGui.Button("Cancel")) {
        File.Delete(progress.Value.outputPath);
        manager.downloadManager.activeDownloads = [..manager.downloadManager.activeDownloads.Where((v) => v.nxm_url != progress.Key)];
        manager.downloadManager.SaveData();
        manager.downloadManager.progresses.Remove(progress.Key, out _);
      }
    }
    ImGui.EndChild();
  }

  private static string modRenamed = string.Empty;

  private static void RenameMod(HD2Mod mod)
  {
    ImGui.TextUnformatted($"Default name: {mod.Name}");
    ImGui.TextUnformatted($"Current name: {manager.modManager.modAliases[mod.Guid]}");
    ImGui.InputText("New name", ref modRenamed, 500);
    if (ImGui.Button("Apply") && modRenamed != "" && modRenamed != string.Empty)
    {
      manager.modManager.modAliases[mod.Guid] = modRenamed;
      modRenamed = string.Empty;
      ImGui.CloseCurrentPopup();
    }
    ImGui.SameLine();
    ImGui.Separator();
    if (ImGui.Button("Cancel"))
    {
      modRenamed = string.Empty;
      ImGui.CloseCurrentPopup();
    }
  }

  private static readonly Dictionary<string, IntPtr> Textures = [];
  private static readonly Dictionary<string, Vector2> TextureDimensions = [];

  private static void DoMod(HD2Mod mod, bool doNexusButton) {
    bool ienabled = manager.modManager.modState[mod.Guid].Enabled;
    bool favourited = manager.modManager.favourites.Contains(mod.Guid);
    bool headerOpen = ImGui.CollapsingHeader((ienabled ? "(X) " : "( ) ") + manager.modManager.modAliases[mod.Guid] + (favourited ? " (Favourited)" : "") + "###" + mod.Guid);
    if (ImGui.BeginPopupContextItem()) {
      if (ImGui.BeginMenu("Add to Modpack")) {
        foreach (KeyValuePair<string, Modpack> pack in manager.modpackManager.modpacks) {
          if (ImGui.Button(pack.Value.Name)) {
            if (mod.ManifestV1.HasValue && mod.ManifestV1.Value.Options != null)
              manager.modpackManager.AddModToModpack(mod.Name, mod.Guid, pack.Key, ModManager.ChoicesToPaths(manager.modManager.processedChoices[mod.Guid]));
            else
              manager.modpackManager.AddModToModpack(mod.Name, mod.Guid, pack.Key, null);
            ImGui.CloseCurrentPopup();
          }
        }
        ImGui.EndMenu();
      }
      if (ImGui.BeginMenu("Rename"))
      {
        RenameMod(mod);
        ImGui.EndMenu();
      }
      if (manager.modManager.favourites.Contains(mod.Guid)) {
        if (ImGui.Button("Unfavourite")) {
          manager.modManager.favourites.Remove(mod.Guid);
          manager.modManager.SaveData();
        }
      }
      else {
        if (ImGui.Button("Favourite")) {
          manager.modManager.favourites.Add(mod.Guid);
          manager.modManager.SaveData();
        }
      }
      ImGui.EndPopup();
    }
    if (mod.ManifestV1.HasValue && mod.ManifestV1.Value.IconPath != null)
    {
      string path = Path.Join(ModManager.ModHolder, mod.FolderName, mod.ManifestV1.Value.IconPath);
      DrawImageTooltip(path, ImGuiHoveredFlags.DelayNormal | ImGuiHoveredFlags.Stationary);
    }
    if (headerOpen) {
      ImGui.Indent();
      ImGui.PushID(mod.Guid);
      ImGui.PushID(mod.GetHashCode());
      if (mod.ManifestV1.HasValue && mod.ManifestV1.Value.IconPath != null)
      {
        string path = Path.Join(ModManager.ModHolder, mod.FolderName, mod.ManifestV1.Value.IconPath);
        if (DrawImage(path, new Vector2(100, 100)) && DrawImageTooltip(path))
          ImGui.SameLine();
      }
      ImGui.BeginGroup();
      bool enabled = ienabled;
      if (ImGui.Checkbox("Enable", ref enabled)) {
        if (enabled) {
          manager.modManager.EnableMod(mod.Guid);
        }
        else {
          manager.modManager.DisableMod(mod.Guid);
          manager.modManager.CheckForPatchGaps();
        }
      }
      if (ImGui.Button("Rename")) {
        ImGui.OpenPopup($"Rename Mod###{mod.Guid}");
      }
      if (ImGui.BeginPopup($"Rename Mod###{mod.Guid}")) {
        RenameMod(mod);
        ImGui.EndPopup();
      }
      if (manager.modManager.favourites.Contains(mod.Guid)) {
        if (ImGui.Button("Unfavourite")) {
          manager.modManager.favourites.Remove(mod.Guid);
          manager.modManager.SaveData();
        }
      }
      else {
        if (ImGui.Button("Favourite")) {
          manager.modManager.favourites.Add(mod.Guid);
          manager.modManager.SaveData();
        }
      }
      if (ImGui.Button("Uninstall")) {
        manager.modManager.UninstallMod(mod.Guid);
        manager.nexusIds.Remove(mod.Guid);
        manager.SaveData();
      }
      ImGui.Button("Add to Modpack");
      ImGui.OpenPopupOnItemClick("ModpackAddition", ImGuiPopupFlags.MouseButtonLeft);
      if (ImGui.BeginPopup("ModpackAddition")) {
        foreach (KeyValuePair<string, Modpack> pack in manager.modpackManager.modpacks) {
          if (ImGui.Button(pack.Value.Name)) {
            if (mod.ManifestV1.HasValue && mod.ManifestV1.Value.Options != null)
              manager.modpackManager.AddModToModpack(mod.Name, mod.Guid, pack.Key, ModManager.ChoicesToPaths(manager.modManager.processedChoices[mod.Guid]));
            else
              manager.modpackManager.AddModToModpack(mod.Name, mod.Guid, pack.Key, null);
            ImGui.CloseCurrentPopup();
          }
        }
        ImGui.EndPopup();
      }
      ImGui.EndGroup();
      ImGui.SameLine();
      ImGui.Spacing();
      ImGui.SameLine();
      ImGui.BeginGroup();
      if (manager.nexusReverse.TryGetValue(mod.Guid, out string reverse)) {
        ImGui.TextUnformatted($"Mod version: {manager.modManager.modState[mod.Guid].Version}");
        if (manager.modManager.modState[mod.Guid].InstalledAt != 0L) {
          ImGui.SameLine();
          ImGui.Spacing();
          ImGui.SameLine();
          ImGui.TextUnformatted($"Installed on {DateTimeOffset.FromUnixTimeMilliseconds(manager.modManager.modState[mod.Guid].InstalledAt):MMMM d, yyyy}");
        }
        if (doNexusButton)
        {
          ImGui.SameLine();
          ImGui.Spacing();
          ImGui.SameLine();
          if (ImGui.Button("Open mod on Nexus"))
          {
            OpenFile($"https://www.nexusmods.com/helldivers2/mods/{reverse}");
          }
        }
      } 
      if (mod.ManifestV1.HasValue) {
        if (mod.ManifestV1.Value.Description != null) {
          ImGui.TextUnformatted(mod.ManifestV1.Value.Description);
        }
        if (mod.ManifestV1.Value.Options != null && mod.ManifestV1.Value.Options.Length > 0) {
          if (mod.ManifestV1.Value.Description != null)
            ImGui.Separator();
          if (ImGui.TreeNodeEx("Options", ImGuiTreeNodeFlags.SpanAvailWidth))
          {
            DrawChoices(mod);
            ImGui.TreePop();
          }
        }
      }
      ImGui.EndGroup();
      ImGui.PopID();
      ImGui.PopID();
      ImGui.Unindent();
      ImGui.Separator();
    }
  }

  private static void DrawChoices(HD2Mod mod) {
    if (!mod.ManifestV1.HasValue) return;
    HDMManifestV1 manifest = mod.ManifestV1.Value;
    if (manifest.Options == null) return;
    ManifestChoices[] choices = manager.modManager.processedChoices[mod.Guid];
    float remaining = ImGui.GetContentRegionAvail().X;
    ImGui.BeginGroup();
    if (ImGui.Button("Enable all options and sub-options")) {
      manager.modManager.ActivateAllOptionsAndSubOptions(mod.Guid);
    }
    if (ImGui.Button("Disable all options and sub-options")) {
      manager.modManager.DisableAllOptionsAndSubOptions(mod.Guid);
    }
    ImGui.EndGroup();
    ImGui.SameLine();
    ImGui.BeginGroup();
    if (ImGui.Button("Enable all sub options")) {
      manager.modManager.EnableAllSubOptions(mod.Guid);
    }
    if (ImGui.Button("Disable all sub options")) {
      manager.modManager.DisableAllSubOptions(mod.Guid);
    }
    ImGui.EndGroup();
    ImGui.SameLine();
    ImGui.BeginGroup();
    if (ImGui.Button("Enable all options")) {
      manager.modManager.ActivateAllOptions(mod.Guid);
    }
    if (ImGui.Button("Disable all options")) {
      manager.modManager.DisableAllOptions(mod.Guid);
    }
    ImGui.EndGroup();
    ImGui.BeginChild($"{mod.Guid}Choices", new(remaining, 400), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
    foreach (ManifestChoices choice in choices) {
      ImGui.TextUnformatted(choice.Name);
      if (choice.IconPath != null)
      {
        string path = Path.Join(ModManager.ModHolder, mod.FolderName, choice.IconPath);
        if (DrawImage(path, new Vector2(100, 100)) && DrawImageTooltip(path))
          ImGui.SameLine();
      }
      ImGui.BeginGroup();
      if (choice.Description != null && choice.Description != "")
        ImGui.TextUnformatted(choice.Description);
      bool enabled = choice.Chosen;
      if (ImGui.Checkbox($"Enable##{mod.Guid}{choice.Name}", ref enabled))
      {
        if (enabled)
        {
          manager.modManager.EnableChoice(mod.Guid, choice.Name!);
        }
        else
        {
          manager.modManager.DisableChoice(mod.Guid, choice.Name!);
        }
      }
      ImGui.EndGroup();
      if (choice.SubChoices != null)
      {
        ImGui.Indent();
        if (ImGui.TreeNodeEx($"Sub Options##{mod.Guid}{choice.Name}", ImGuiTreeNodeFlags.SpanAvailWidth)) {
          ImGui.Indent();
          DrawSubChoices(choice.SubChoices, mod.Guid, mod.FolderName, choice.Name!);
          ImGui.Unindent();
          ImGui.TreePop();
        }
        ImGui.Unindent();
      }
      ImGui.Spacing();
      ImGui.Separator();
      ImGui.Spacing();
    }
    ImGui.EndChild();
  }

  private static void DrawSubChoices(ManifestChoices[] subChoices, string mod, string folderName, string currentPath) {
    foreach (ManifestChoices choice in subChoices) {
      ImGui.TextUnformatted(choice.Name);
      if (choice.IconPath != null)
      {
        string path = Path.Join(ModManager.ModHolder, folderName, choice.IconPath);
        bool succeeded = true;
        succeeded = succeeded && DrawImage(path, new Vector2(100, 100));
        succeeded = succeeded && DrawImageTooltip(path);
        if (succeeded)
          ImGui.SameLine();
      }
      ImGui.BeginGroup();
      if (choice.Description != null && choice.Description != "")
        ImGui.TextUnformatted(choice.Description);
      bool enabled = choice.Chosen;
      if (ImGui.Checkbox($"Enable##{mod}{currentPath}{choice.Name}", ref enabled))
      {
        if (enabled)
        {
          manager.modManager.EnableChoice(mod, currentPath + "/" + choice.Name);
        }
        else
        {
          manager.modManager.DisableChoice(mod, currentPath + "/" + choice.Name);
        }
      }
      ImGui.EndGroup();
      ImGui.Spacing();
      ImGui.Separator();
      ImGui.Spacing();
    }
  }

  private static bool DrawImage(string path, Vector2 size) {
    IntPtr ptr = GetImagePointer(path);
    if (ptr == IntPtr.Zero) return false;
    ImGui.Image(ptr, size);
    return true;
  }

  private static bool DrawImageTooltip(string path, ImGuiHoveredFlags flags = ImGuiHoveredFlags.None) {
    IntPtr ptr = GetImagePointer(path);
    if (ptr == IntPtr.Zero) return false;
    if (ImGui.IsItemHovered(flags)) {
      Vector2 imageSize = TextureDimensions[path];
      Vector2 displaySize = ImGui.GetIO().DisplaySize;
      Vector2 stylePadding = ImGui.GetStyle().WindowPadding;
      Vector2 maxSize = new(
        displaySize.X * 0.9f,
        displaySize.Y * 0.9f
      );
      Vector2 imgSize = new();
      if (imageSize.X > maxSize.X || imageSize.Y > maxSize.Y)
      {
        if (maxSize.X / maxSize.Y > imageSize.X / imageSize.Y)
        {
          imgSize.Y = maxSize.Y;
          imgSize.X = imgSize.Y * imageSize.X / imageSize.Y;
        }
        else
        {
          imgSize.X = maxSize.X;
          imgSize.Y = imgSize.X * imageSize.Y / imageSize.X;
        }
        Vector2 tooltipSize = new(
          imgSize.X + stylePadding.X * 2,
          imgSize.Y + stylePadding.Y * 2
        );
        Vector2 mp = ImGui.GetIO().MousePos;
        Vector2 pos = new(
          mp.X + 10,
          mp.Y + 10
        );
        if (pos.X + tooltipSize.X > displaySize.X)
          pos.X = displaySize.X - tooltipSize.X;
        if (pos.Y + tooltipSize.Y > displaySize.Y)
          pos.Y = displaySize.Y - tooltipSize.Y;
        if (pos.X < 0) pos.X = 0;
        if (pos.Y < 0) pos.Y = 0;
        ImGui.SetNextWindowSize(tooltipSize);
        ImGui.SetNextWindowPos(pos);
      }
      else {
        imgSize = imageSize;
      }
      ImGui.BeginTooltip();
      ImGui.Image(ptr, imgSize);
      ImGui.EndTooltip();
    }
    return true;
  }

  private static string ResolveFilePath(string path) {
    if (File.Exists(path))
      return path;
    FileInfo f = new(path);
    string fileName = f.Name.ToLower();
    foreach (string file in Directory.EnumerateFiles(f.DirectoryName!)) {
      FileInfo f1 = new(file);
      if (f1.Name.Equals(fileName, StringComparison.CurrentCultureIgnoreCase)) {
        return file;
      }
    }
    return string.Empty;
  }

  private static IntPtr GetImagePointer(string filePath) {
    if (!Textures.TryGetValue(filePath, out nint value)) {
      string path = ResolveFilePath(filePath);
      if (path == string.Empty) return IntPtr.Zero;
      try
      {
        // god knows why this is faster
        byte[] imageBytes = File.ReadAllBytes(path);
        Image<Rgba32> img = Image.Load<Rgba32>(imageBytes);
        ImageSharpTexture texture = new(img, false);
        TextureDimensions[filePath] = new Vector2(texture.Width, texture.Height);
        Texture deviceTex = texture.CreateDeviceTexture(gd, gd.ResourceFactory);
        IntPtr ptr = controller.GetOrCreateImGuiBinding(gd.ResourceFactory, deviceTex);
        Textures[filePath] = ptr;
        return ptr;
      }
      catch (UnknownImageFormatException) {
        return IntPtr.Zero;
      }
    }
    return value;
  }

  private static string DataPathStr = string.Empty;

  private static void PromptForDataPath() {
    if (ImGui.BeginPopupModal("Data Path", ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.TextUnformatted("HD2 data path is unspecified or does not exist.");
      if (ImGui.InputText("Path to Helldivers 2/data", ref DataPathStr, 500, ImGuiInputTextFlags.EnterReturnsTrue)) {
        if (IsValidHD2Directory(DataPathStr)) {
          File.WriteAllText(HD2PathFile, DataPathStr);
          NeedsHD2DataPath = false;
          if (!NeedsKey) {
            StartManager();
            UseHardlinks = !OperatingSystem.IsLinux() && Path.GetPathRoot(File.ReadAllText(HD2PathFile).Trim()) == Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
          }
          ImGui.CloseCurrentPopup();
        }
        else {
          DataPathStr = "Path specified is invalid or does not exist.";
        }
      }
      ImGui.EndPopup();
    }
  }

  private static bool showFailure = false;
  private static long failureTime;

  private static void PromptForKey()
  {
    ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    if (ImGui.BeginPopupModal("Key Prompt", ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.TextUnformatted("Nexus API key is missing or invalid.");
      if (ImGui.Button("Open API keys page (needs personal key, at the bottom)")) {
        if (OperatingSystem.IsLinux()) {
          System.Diagnostics.Process.Start("xdg-open", "\"https://next.nexusmods.com/settings/api-keys\"");
        }
        else {
          System.Diagnostics.Process.Start("explorer.exe", "\"https://next.nexusmods.com/settings/api-keys\"");
        }
      }
      ImGui.TextUnformatted($"Make a plaintext file at {KeyFile} and input your API key.");
      if (ImGui.Button("Open file"))
      {
        if (OperatingSystem.IsLinux()) {
          using (FileStream f = File.Create(KeyFile)) {
            StreamWriter w = new(f);
            w.Write("Replace-With-Key");
            w.Flush();
          }
          System.Diagnostics.Process.Start("xdg-open", $"\"{KeyFile}\"");
        }
        else {
          using (FileStream f = File.Create(KeyFile)) {}
          System.Diagnostics.Process.Start("explorer.exe", $"\"{KeyFile}\"");
        }
      }
      if (showFailure) {
        ImGui.Text($"{DateTimeOffset.FromUnixTimeSeconds(failureTime):HH:mm:ss} Key file does not exist or API key is invalid.");
      }
      if (ImGui.Button("Key file created?"))
      {
        if (IsValidAPIKey())
        {
          NeedsKey = false;
          if (!NeedsHD2DataPath) {
            StartManager();
            UseHardlinks = !OperatingSystem.IsLinux() && Path.GetPathRoot(File.ReadAllText(HD2PathFile).Trim()) == Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
          }
          ImGui.CloseCurrentPopup();
        }
        else {
          showFailure = true;
          failureTime = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
        }
      }
      ImGui.EndPopup();
    }
  }
}

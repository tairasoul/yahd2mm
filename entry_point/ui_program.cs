using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;
using Veldrid.ImageSharp;
using Humanizer;
using FuzzySharp;
using System.IO.Pipes;
using System.Text;
using SixLabors.ImageSharp;

namespace yahd2mm;

partial class EntryPoint
{
  private static bool NeedsKey = !File.Exists(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "key.txt"));
  private static bool NeedsHD2DataPath = !(File.Exists(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "path.txt")) && Directory.Exists(File.ReadAllText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "path.txt")).Trim()));
  private static Sdl2Window window;
  private static GraphicsDevice gd;
  private static CommandList cl;
  private static ImGuiRenderer controller;
  public static void RunMain()
  {
    if (!Directory.Exists(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm")))
    {
      Directory.CreateDirectory(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm"));
    }
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
    queue = OperatingSystem.IsLinux() ? new FilesystemQueue() : new WindowsFilesystemQueue();
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
    if (!NeedsKey && !NeedsHD2DataPath)
    {
      StartManager();
    }
    if (OperatingSystem.IsWindows()) {
      UserKnowsUACIsForSymlinking = Task.Run(async () =>
      {
        while (!UserHasConfirmedUAC) {
          await Task.Delay(500);
        }
      });
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

  private static void StartManager()
  {
    APIKey = File.ReadAllText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "key.txt")).Trim();
    HD2Path = File.ReadAllText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "path.txt")).Trim();
    manager = new();
    manager.BeginListeningPipe();
  }

  internal static bool SwitchToDownloads = false;
  internal static bool UserHasConfirmedUAC = OperatingSystem.IsLinux();
  internal static Task UserKnowsUACIsForSymlinking = null;
  private static void DoUI()
  {
    ImGui.SetNextWindowSize(new Vector2(window.Width, window.Height));
    ImGui.SetNextWindowPos(new Vector2(0));
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
    ImGui.Begin("yahd2mm", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);
    if (!NeedsKey && !NeedsHD2DataPath && UserHasConfirmedUAC)
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
      if (SwitchToDownloads)
      {
        if (ImGui.BeginTabItem("Downloads", ref p_open, ImGuiTabItemFlags.SetSelected))
        {
          if (SwitchToDownloads)
          {
            SwitchToDownloads = false;
          }
          DoDownloads();
          ImGui.EndTabItem();
        }
      }
      else {
        if (ImGui.BeginTabItem("Downloads"))
        {
          if (SwitchToDownloads)
          {
            SwitchToDownloads = false;
          }
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
    if (!NeedsHD2DataPath && !NeedsKey && !UserHasConfirmedUAC) {
      ImGui.OpenPopup("Administrator Privileges");
    }
    DoSymlinkPopup();
    ImGui.End();
    ImGui.PopStyleVar(1);
  }

  private static void DoSymlinkPopup() {
    ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    if (ImGui.BeginPopupModal("Administrator Privileges", ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.Text("yahd2mm needs to launch a child process with admin privileges to create symlinks and correctly deploy mods.");
      ImGui.Text("It cannot correctly deploy mods otherwise (copying is unimplemented).");
      ImGui.Text("A prompt for admin privileges will appear soon after confirming.");
      if (ImGui.Button("I understand."))
      {
        UserHasConfirmedUAC = true;
        ImGui.CloseCurrentPopup();
      }
      ImGui.EndPopup();
    }
  }

  private static void DoCompletedDownloads() {
    if (ImGui.Button("Clear")) {
      manager.completed.Clear();
    }
    ImGui.BeginChild("ScrollableDownloads", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    foreach (FinishedDownload progress in manager.completed.Values.ToList())
    {
      DoCompletedDownload(progress);
    }
    ImGui.EndChild();
  }

  private static void DoCompletedDownload(FinishedDownload progress) {
    float width = ImGui.GetContentRegionAvail().X;
    ImGui.BeginChild("download" + progress.Filename, new Vector2(width, 100), ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AutoResizeX);
    ImGui.Text($"Filename: {progress.Filename}");
    ImGui.Text($"Mod name: {progress.Modname}");
    ImGui.Text($"Mod size: {new Humanizer.Bytes.ByteSize(progress.Size).Humanize()}");
    ImGui.EndChild();
  }

  private static int draggedIndex = -1;

  private static void DoPriorities() {
    if (ImGui.Button("Re-deploy with priority list")) {
      manager.modManager.ApplyPriorities();
    }
    ImGui.BeginChild("PriorityListChild", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    for (int i = 0; i < manager.modManager.priorities.Length; i++)
    {
      string basename = manager.modManager.priorities[i];
      string item = manager.modManager.modAliases[basename];
      ImGui.Selectable($"{(manager.modManager.modState[basename].Enabled ? "(X) " : "( ) ")}{i}: {item}###{basename}", false);

      if (ImGui.BeginDragDropSource())
      {
        draggedIndex = i;
        ImGui.SetDragDropPayload("MOD_PRIORITY_ITEM", IntPtr.Zero, 0);
        ImGui.Text("Dragging: " + item);
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
          }
        }
        ImGui.EndDragDropTarget();
      }
    }
    ImGui.EndChild();
  }

  private static void DoLocalFiles() {
    if (ImGui.Button("Open Folder")) {
      if (OperatingSystem.IsWindows()) {
        System.Diagnostics.Process.Start("explorer.exe", "/select," + $"\"{LocalFileHolder.LocalFiles}\"");
      }
      else if (OperatingSystem.IsLinux()) {
        System.Diagnostics.Process.Start("xdg-open", $"\"{LocalFileHolder.LocalFiles}\"");
      }
    }
    ImGui.BeginChild("ScrollableLocalFiles", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    foreach (string file in files.localFiles) {
      ImGui.BeginChild($"localFile{file}", new(ImGui.GetContentRegionAvail().X, 120), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AlwaysAutoResize);
      ImGui.BeginChild($"localFile{file}Name", new(ImGui.GetContentRegionAvail().X, 60), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysHorizontalScrollbar);
      ImGui.Text(Path.GetFileName(file));
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
      foreach (ArsenalMod mod in manager.modManager.mods.ToList())
      {
        manager.modManager.EnableMod(mod.Guid);
      }
    }
    ImGui.SameLine();
    if (ImGui.Button("Disable all mods"))
    {
      foreach (ArsenalMod mod in manager.modManager.mods.ToList())
      {
        manager.modManager.DisableMod(mod.Guid);
      }
    }
    ImGui.SameLine();
    if (ImGui.Button("Re-deploy"))
    {
      List<ArsenalMod> enabled = [];
      foreach (ArsenalMod mod in manager.modManager.mods.ToList())
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
      foreach (ArsenalMod mod in enabled) {
        manager.modManager.EnableMod(mod.Guid);
      }
    }
    ImGui.SameLine();
    if (ImGui.Button("Purge mod files")) {
      foreach (string modFile in Directory.EnumerateFiles(HD2Path).Where((v) => ModManager.FileNumRegex.Match(v).Success)) {
        queue.Delete(modFile);
      }
    }
    ImGui.InputText("Search", ref SearchingString, 80, ImGuiInputTextFlags.EscapeClearsAll | ImGuiInputTextFlags.AlwaysOverwrite);
    ImGui.Text("Priority list is ignored when enabling/disabling mods here, apply in Priorities!");
    ImGui.BeginChild("ScrollableModlist", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    ArsenalMod[] mods = [.. manager.modManager.mods];
    Array.Sort(mods, (x, y) => string.Compare(manager.modManager.modAliases[x.Guid], manager.modManager.modAliases[y.Guid]));
    mods = [.. mods.OrderByDescending((v) => manager.modManager.favourites.Contains(v.Guid))];
    if (SearchingString != "" && SearchingString != string.Empty)
    {
      mods = mods
      .Where(mod => manager.modManager.modAliases[mod.Guid].Contains(SearchingString, StringComparison.OrdinalIgnoreCase))
      .Select(static mod => new
      {
        Mod = mod,
        Score = Fuzz.Ratio(mod.Name, SearchingString),
        IsPrefix = manager.modManager.modAliases[mod.Guid].StartsWith(SearchingString, StringComparison.OrdinalIgnoreCase),
        Contains = manager.modManager.modAliases[mod.Guid].Contains(SearchingString, StringComparison.OrdinalIgnoreCase)
      })
      .OrderByDescending(x => x.IsPrefix ? 2 : (x.Contains ? 1 : 0))
      .ThenByDescending(x => x.Score)
      .ThenBy(mod => manager.modManager.modAliases[mod.Mod.Guid].Length).OrderByDescending((v) => manager.modManager.favourites.Contains(v.Mod.Guid)).Select((x) => x.Mod).ToArray();
    }
    foreach (ArsenalMod mod in mods)
    {
      DoMod(mod);
    }
    ImGui.EndChild();
  }

  private static void DoDownloads()
  {
    ImGui.BeginChild("ScrollableDownloads", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Borders, ImGuiWindowFlags.AlwaysVerticalScrollbar);
    foreach (DownloadProgress progress in manager.progresses.Values.ToList())
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
        foreach (ModpackMod mod in packs.Value.mods)
        {
          DoModpackMod(mod, packs.Key);
        }
        if (ImGui.Button("Remove all mods"))
        {
          foreach (ModpackMod mod in packs.Value.mods)
          {
            manager.modpackManager.RemoveModFromModpack(mod.guid, packs.Key);
          }
        }
        if (ImGui.Button("Add active mods")) {
          foreach (ArsenalMod amod in manager.modManager.mods.ToList())
          {
            if (manager.modManager.modState.TryGetValue(amod.Guid, out ModJson json))
              if (json.Enabled)
              {
                bool modHasChoices = manager.modManager.processedChoices.TryGetValue(amod.Guid, out ManifestChoices[]? choices);
                manager.modpackManager.AddModToModpack(amod.Name, amod.Guid, packs.Key, modHasChoices ? ModManager.ChoicesToPaths(choices!) : null);
              }
          }
        }
        if (ImGui.Button("Load modpack"))
        {
          manager.modpackManager.LoadModpack(packs.Key, manager.modManager);
        }
        if (ImGui.Button("Delete modpack"))
        {
          manager.modpackManager.DeleteModpack(packs.Key);
        }
        ImGui.PopID();
        ImGui.Unindent();
        ImGui.Separator();
      }
    }
    ImGui.EndChild();
  }
  private static void DoModpackMod(ModpackMod mod, string name)
  {
    ImGui.PushID(mod.guid);
    if (ImGui.TreeNodeEx(mod.name))
    {
      if (mod.options != null)
      {
        ImGui.Text("Enabled options:");
        ImGui.Indent();
        foreach (string opt in mod.options)
        {
          ImGui.Text(opt);
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
        foreach (ArsenalMod mod in manager.modManager.mods.ToList())
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

  private static void DoDownload(DownloadProgress progress)
  {
    float width = ImGui.GetContentRegionAvail().X;
    ImGui.BeginChild("download" + progress.modName, new Vector2(width, 80), ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AutoResizeX);
    ImGui.Text(progress.modName);
    ImGui.ProgressBar(progress.BytesDownloaded / progress.TotalBytes, Vector2.Zero, $"{new Humanizer.Bytes.ByteSize(progress.BytesDownloaded).Humanize()}/{new Humanizer.Bytes.ByteSize(progress.TotalBytes).Humanize()}");
    ImGui.EndChild();
  }

  private static string modRenamed = string.Empty;

  private static void RenameMod(ArsenalMod mod)
  {
    ImGui.Text($"Default name: {mod.Name}");
    ImGui.Text($"Current name: {manager.modManager.modAliases[mod.Guid]}");
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

  private static void DoMod(ArsenalMod mod) {
    bool ienabled = manager.modManager.modState[mod.Guid].Enabled;
    bool favourited = manager.modManager.favourites.Contains(mod.Guid);
    /*if (ienabled) {
      ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0.5f, 0f, 1f));
      ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0, 0.6f, 0.2f, 1f));
      ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.1f, 0.65f, 0.3f, 1f));
    }*/
    bool headerOpen = ImGui.CollapsingHeader((ienabled ? "(X) " : "( ) ") + manager.modManager.modAliases[mod.Guid] + (favourited ? " (Favourited)" : "") + "###" + mod.Guid);
    if (ImGui.BeginPopupContextItem()) {
      if (ImGui.BeginMenu("Add to Modpack")) {
        foreach (KeyValuePair<string, Modpack> pack in manager.modpackManager.modpacks) {
          if (ImGui.Button(pack.Value.Name)) {
            if (mod.Manifest.HasValue && mod.Manifest.Value.Options != null)
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
    if (mod.Manifest.HasValue && mod.Manifest.Value.IconPath != null)
    {
      string path = Path.Join(ModManager.ModHolder, mod.FolderName, mod.Manifest.Value.IconPath);
      DrawImageTooltip(path, ImGuiHoveredFlags.DelayNormal | ImGuiHoveredFlags.Stationary);
    }
    if (headerOpen) {
      ImGui.Indent();
      ImGui.PushID(mod.Guid);
      ImGui.PushID(mod.GetHashCode());
      /*if (ienabled) {
        ImGui.PopStyleColor(3);
      }*/
      if (mod.Manifest.HasValue && mod.Manifest.Value.IconPath != null)
      {
        string path = Path.Join(ModManager.ModHolder, mod.FolderName, mod.Manifest.Value.IconPath);
        bool succeeded = true;
        succeeded = succeeded && DrawImage(path, new Vector2(100, 100));
        succeeded = succeeded && DrawImageTooltip(path);
        if (succeeded)
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
        }
      }
      else {
        if (ImGui.Button("Favourite")) {
          manager.modManager.favourites.Add(mod.Guid);
        }
      }
      if (ImGui.Button("Uninstall")) {
        manager.modManager.UninstallMod(mod.Guid);
      }
      ImGui.EndGroup();
      if (mod.Manifest.HasValue) {
        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
        ImGui.BeginGroup();
        if (mod.Manifest.Value.Description != null) {
          ImGui.Text(mod.Manifest.Value.Description);
        }
        if (mod.Manifest.Value.Options != null && mod.Manifest.Value.Options.Length > 0)
          if (ImGui.CollapsingHeader("Options"))
          {
            DrawChoices(mod);
          }
        ImGui.EndGroup();
      }
      ImGui.PopID();
      ImGui.PopID();
      ImGui.Unindent();
      ImGui.Separator();
    }
    else {
      /*if (ienabled) {
        ImGui.PopStyleColor(3);
      }*/
    }
  }

  private static void DrawChoices(ArsenalMod mod) {
    if (!mod.Manifest.HasValue) return;
    ArsenalManifest manifest = mod.Manifest.Value;
    if (manifest.Options == null) return;
    ManifestChoices[] choices = manager.modManager.processedChoices[mod.Guid];
    float remaining = ImGui.GetContentRegionAvail().X;
    ImGui.BeginChild($"{mod.Guid}Choices", new(remaining, 400), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
    foreach (ManifestChoices choice in choices) {
      ImGui.Text(choice.Name);
      if (choice.IconPath != null)
      {
        string path = Path.Join(ModManager.ModHolder, mod.FolderName, choice.IconPath);
        bool succeeded = true;
        succeeded = succeeded && DrawImage(path, new Vector2(100, 100));
        succeeded = succeeded && DrawImageTooltip(path);
        if (succeeded)
          ImGui.SameLine();
      }
      ImGui.BeginGroup();
      if (choice.Description != null && choice.Description != "")
        ImGui.Text(choice.Description);
      bool enabled = choice.Chosen;
      if (ImGui.Checkbox($"Enable##{mod.Guid}{choice.Name}", ref enabled))
      {
        if (enabled)
        {
          manager.modManager.EnableChoice(mod.Guid, choice.Name);
        }
        else
        {
          manager.modManager.DisableChoice(mod.Guid, choice.Name);
        }
      }
      ImGui.EndGroup();
      if (choice.SubChoices != null) {
        ImGui.Indent();
        ImGui.Text("Sub Options");
        ImGui.Indent();
        DrawSubChoices(choice.SubChoices, mod.Guid, mod.FolderName, choice.Name);
        ImGui.Unindent();
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
      ImGui.Text(choice.Name);
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
        ImGui.Text(choice.Description);
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
        ImageSharpTexture texture = new(path);
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
      ImGui.Text("HD2 data path is unspecified or does not exist.");
      if (ImGui.InputText("Path to Helldivers 2/data", ref DataPathStr, 500, ImGuiInputTextFlags.EnterReturnsTrue)) {
        if (Directory.Exists(DataPathStr)) {
          File.WriteAllText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "path.txt"), DataPathStr);
          NeedsHD2DataPath = false;
          if (!NeedsKey) {
            StartManager();
          }
          ImGui.CloseCurrentPopup();
        }
      }
      ImGui.EndPopup();
    }
  }

  private static void PromptForKey()
  {
    ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
    if (ImGui.BeginPopupModal("Key Prompt", ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.AlwaysAutoResize))
    {
      ImGui.Text("Nexus API key is required.");
      ImGui.Text($"Make a plaintext file at {Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "key.txt")} and input your API key.");
      if (ImGui.Button("Copy folder path to clipboard"))
      {
        ImGui.SetClipboardText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm"));
      }
      if (ImGui.Button("Key file created?"))
      {
        if (File.Exists(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yahd2mm", "key.txt")))
        {
          NeedsKey = false;
          if (!NeedsHD2DataPath) {
            StartManager();
          }
          ImGui.CloseCurrentPopup();
        }
      }
      ImGui.EndPopup();
    }
  }
}

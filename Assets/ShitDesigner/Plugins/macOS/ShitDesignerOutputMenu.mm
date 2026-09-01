#include <array>
#include <cstdint>
#include <deque>
#include <dlfcn.h>

namespace {
using NativeObject = void *;
using NativeSelector = void *;
using NativeClass = void *;
using NativeImplementation = void (*)();

enum OutputMenuCommand {
  StartProgramOutput = 0,
  StopProgramOutput = 1,
  StartOverlayOutput = 2,
  StopOverlayOutput = 3,
  ToggleTestPattern = 4,
  SwapOutputs = 5,
  SetScalingStretch = 6,
  SetScalingFill = 7,
  SetScalingFit = 8,
  SetEmulationDisplay = 9,
  SetEmulation16x9 = 10,
  SetEmulation16x10 = 11,
  SetEmulation4x3 = 12,
  SetEmulation3x4 = 13,
  SetEmulation1x1 = 14,
  SetEmulation9x16 = 15,
  SetEmulation21x9 = 16,
  SetEmulation5x2 = 17,
  SetEmulation4_5x1 = 18
};

static void *s_appKitLibrary;
static void *s_objectiveCLibrary;
static NativeObject s_mainMenu;
static NativeObject s_topItem;
static NativeObject s_submenu;
static NativeObject s_target;
static NativeObject s_startProgramItem;
static NativeObject s_stopProgramItem;
static NativeObject s_startOverlayItem;
static NativeObject s_stopOverlayItem;
static NativeObject s_identifyItem;
static NativeObject s_swapItem;
static NativeObject s_scalingItem;
static NativeObject s_scalingMenu;
static NativeObject s_scalingStretchItem;
static NativeObject s_scalingFillItem;
static NativeObject s_scalingFitItem;
static NativeObject s_emulationItem;
static NativeObject s_emulationMenu;
static std::array<NativeObject, 10> s_emulationItems{};
static std::deque<int> s_commands;

using GetClass = NativeClass (*)(const char *);
using RegisterSelector = NativeSelector (*)(const char *);
using AllocateClassPair = NativeClass (*)(NativeClass, const char *,
                                          std::size_t);
using RegisterClassPair = void (*)(NativeClass);
using AddMethod = bool (*)(NativeClass, NativeSelector, NativeImplementation,
                           const char *);

static GetClass s_getClass;
static RegisterSelector s_registerSelector;
static AllocateClassPair s_allocateClassPair;
static RegisterClassPair s_registerClassPair;
static AddMethod s_addMethod;
static void *s_sendMessage;
static bool s_runtimeReady;

template <typename ReturnType, typename... ArgumentTypes>
ReturnType SendMessage(NativeObject receiver, const char *selectorName,
                       ArgumentTypes... arguments) {
  using Send = ReturnType (*)(NativeObject, NativeSelector, ArgumentTypes...);
  return reinterpret_cast<Send>(s_sendMessage)(
      receiver, s_registerSelector(selectorName), arguments...);
}

NativeObject CreateString(const char *value) {
  const auto stringClass = s_getClass("NSString");
  const auto instance = SendMessage<NativeObject>(stringClass, "alloc");
  return SendMessage<NativeObject>(instance, "initWithUTF8String:", value);
}

void Release(NativeObject value) {
  if (value != nullptr)
    SendMessage<void>(value, "release");
}

void HandleOutputMenuItem(NativeObject, NativeSelector, NativeObject sender) {
  const auto tag = SendMessage<std::intptr_t>(sender, "tag");
  if (tag >= StartProgramOutput && tag <= SetEmulation4_5x1)
    s_commands.push_back(static_cast<int>(tag));
}

NativeClass GetOrCreateTargetClass() {
  const auto className = "ShitDesignerOutputMenuTarget";
  auto targetClass = s_getClass(className);
  if (targetClass != nullptr)
    return targetClass;
  targetClass = s_allocateClassPair(s_getClass("NSObject"), className, 0);
  if (targetClass == nullptr)
    return nullptr;
  if (!s_addMethod(targetClass, s_registerSelector("handleOutputMenuItem:"),
                   reinterpret_cast<NativeImplementation>(HandleOutputMenuItem),
                   "v@:@"))
    return nullptr;
  s_registerClassPair(targetClass);
  return targetClass;
}

NativeObject CreateMenuItem(const char *title, NativeSelector action,
                            OutputMenuCommand command) {
  const auto nativeTitle = CreateString(title);
  const auto keyEquivalent = CreateString("");
  const auto instance =
      SendMessage<NativeObject>(s_getClass("NSMenuItem"), "alloc");
  const auto item = SendMessage<NativeObject>(
      instance, "initWithTitle:action:keyEquivalent:", nativeTitle, action,
      keyEquivalent);
  Release(nativeTitle);
  Release(keyEquivalent);
  if (item != nullptr)
    SendMessage<void>(item, "setTag:", static_cast<std::intptr_t>(command));
  return item;
}

bool EnsureRuntime() {
  if (s_runtimeReady)
    return true;
  s_appKitLibrary = dlopen("/System/Library/Frameworks/AppKit.framework/AppKit",
                           RTLD_LAZY | RTLD_LOCAL);
  s_objectiveCLibrary =
      dlopen("/usr/lib/libobjc.A.dylib", RTLD_LAZY | RTLD_LOCAL);
  if (s_appKitLibrary == nullptr || s_objectiveCLibrary == nullptr)
    return false;
  s_getClass =
      reinterpret_cast<GetClass>(dlsym(s_objectiveCLibrary, "objc_getClass"));
  s_registerSelector = reinterpret_cast<RegisterSelector>(
      dlsym(s_objectiveCLibrary, "sel_registerName"));
  s_allocateClassPair = reinterpret_cast<AllocateClassPair>(
      dlsym(s_objectiveCLibrary, "objc_allocateClassPair"));
  s_registerClassPair = reinterpret_cast<RegisterClassPair>(
      dlsym(s_objectiveCLibrary, "objc_registerClassPair"));
  s_addMethod = reinterpret_cast<AddMethod>(
      dlsym(s_objectiveCLibrary, "class_addMethod"));
  s_sendMessage = dlsym(s_objectiveCLibrary, "objc_msgSend");
  s_runtimeReady = s_getClass != nullptr && s_registerSelector != nullptr &&
                   s_allocateClassPair != nullptr &&
                   s_registerClassPair != nullptr && s_addMethod != nullptr &&
                   s_sendMessage != nullptr;
  return s_runtimeReady;
}
} // namespace

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuCreate(void) {
  if (s_topItem != nullptr || !EnsureRuntime())
    return;
  const auto application = SendMessage<NativeObject>(
      s_getClass("NSApplication"), "sharedApplication");
  s_mainMenu = SendMessage<NativeObject>(application, "mainMenu");
  const auto targetClass = GetOrCreateTargetClass();
  if (s_mainMenu == nullptr || targetClass == nullptr)
    return;
  s_target = SendMessage<NativeObject>(
      SendMessage<NativeObject>(targetClass, "alloc"), "init");
  const auto nativeTitle = CreateString("Output");
  s_submenu = SendMessage<NativeObject>(
      SendMessage<NativeObject>(s_getClass("NSMenu"), "alloc"),
      "initWithTitle:", nativeTitle);
  Release(nativeTitle);
  SendMessage<void>(s_submenu,
                    "setAutoenablesItems:", static_cast<signed char>(false));
  const auto action = s_registerSelector("handleOutputMenuItem:");
  s_startProgramItem =
      CreateMenuItem("Start Output 1 (Program)", action, StartProgramOutput);
  s_stopProgramItem =
      CreateMenuItem("Stop Output 1 (Program)", action, StopProgramOutput);
  s_startOverlayItem =
      CreateMenuItem("Start Output 2 (Overlay)", action, StartOverlayOutput);
  s_stopOverlayItem =
      CreateMenuItem("Stop Output 2 (Overlay)", action, StopOverlayOutput);
  s_identifyItem =
      CreateMenuItem("Display Test Pattern", action, ToggleTestPattern);
  s_swapItem = CreateMenuItem("Swap Output Displays", action, SwapOutputs);

  const auto scalingTitle = CreateString("Scaling");
  s_scalingMenu = SendMessage<NativeObject>(
      SendMessage<NativeObject>(s_getClass("NSMenu"), "alloc"),
      "initWithTitle:", scalingTitle);
  Release(scalingTitle);
  SendMessage<void>(s_scalingMenu,
                    "setAutoenablesItems:", static_cast<signed char>(false));
  s_scalingStretchItem =
      CreateMenuItem("Stretch", action, SetScalingStretch);
  s_scalingFillItem = CreateMenuItem("Fill", action, SetScalingFill);
  s_scalingFitItem = CreateMenuItem("Fit", action, SetScalingFit);
  s_scalingItem = CreateMenuItem("Scaling", nullptr, SetScalingFill);
  SendMessage<void>(s_scalingItem, "setSubmenu:", s_scalingMenu);

  const auto emulationTitle = CreateString("Emulation");
  s_emulationMenu = SendMessage<NativeObject>(
      SendMessage<NativeObject>(s_getClass("NSMenu"), "alloc"),
      "initWithTitle:", emulationTitle);
  Release(emulationTitle);
  SendMessage<void>(s_emulationMenu,
                    "setAutoenablesItems:", static_cast<signed char>(false));
  const std::array<const char *, 10> emulationTitles{
      "Native Display", "16:9", "16:10", "4:3",  "3:4", "1:1",
      "9:16",           "21:9", "5:2",   "4.5:1"};
  const std::array<OutputMenuCommand, 10> emulationCommands{
      SetEmulationDisplay, SetEmulation16x9, SetEmulation16x10,
      SetEmulation4x3,     SetEmulation3x4,  SetEmulation1x1,
      SetEmulation9x16,    SetEmulation21x9, SetEmulation5x2,
      SetEmulation4_5x1};
  for (std::size_t index = 0; index < s_emulationItems.size(); ++index) {
    s_emulationItems[index] =
        CreateMenuItem(emulationTitles[index], action, emulationCommands[index]);
    SendMessage<void>(s_emulationItems[index], "setTarget:", s_target);
    SendMessage<void>(s_emulationMenu, "addItem:", s_emulationItems[index]);
  }
  s_emulationItem = CreateMenuItem("Emulation", nullptr, SetEmulationDisplay);
  SendMessage<void>(s_emulationItem, "setSubmenu:", s_emulationMenu);
  for (const auto item : {s_startProgramItem, s_stopProgramItem,
                          s_startOverlayItem, s_stopOverlayItem,
                          s_identifyItem, s_swapItem, s_scalingStretchItem,
                          s_scalingFillItem, s_scalingFitItem})
    SendMessage<void>(item, "setTarget:", s_target);
  SendMessage<void>(s_scalingMenu, "addItem:", s_scalingStretchItem);
  SendMessage<void>(s_scalingMenu, "addItem:", s_scalingFillItem);
  SendMessage<void>(s_scalingMenu, "addItem:", s_scalingFitItem);
  SendMessage<void>(s_submenu, "addItem:", s_startProgramItem);
  SendMessage<void>(s_submenu, "addItem:", s_stopProgramItem);
  auto separator =
      SendMessage<NativeObject>(s_getClass("NSMenuItem"), "separatorItem");
  SendMessage<void>(s_submenu, "addItem:", separator);
  SendMessage<void>(s_submenu, "addItem:", s_startOverlayItem);
  SendMessage<void>(s_submenu, "addItem:", s_stopOverlayItem);
  separator =
      SendMessage<NativeObject>(s_getClass("NSMenuItem"), "separatorItem");
  SendMessage<void>(s_submenu, "addItem:", separator);
  SendMessage<void>(s_submenu, "addItem:", s_scalingItem);
  SendMessage<void>(s_submenu, "addItem:", s_emulationItem);
  separator =
      SendMessage<NativeObject>(s_getClass("NSMenuItem"), "separatorItem");
  SendMessage<void>(s_submenu, "addItem:", separator);
  SendMessage<void>(s_submenu, "addItem:", s_swapItem);
  SendMessage<void>(s_submenu, "addItem:", s_identifyItem);
  s_topItem = CreateMenuItem("Output", nullptr, StartProgramOutput);
  SendMessage<void>(s_topItem, "setSubmenu:", s_submenu);
  SendMessage<void>(s_mainMenu, "addItem:", s_topItem);
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuDestroy(void) {
  if (s_mainMenu != nullptr && s_topItem != nullptr)
    SendMessage<void>(s_mainMenu, "removeItem:", s_topItem);
  Release(s_startProgramItem);
  Release(s_stopProgramItem);
  Release(s_startOverlayItem);
  Release(s_stopOverlayItem);
  Release(s_identifyItem);
  Release(s_swapItem);
  Release(s_scalingStretchItem);
  Release(s_scalingFillItem);
  Release(s_scalingFitItem);
  Release(s_scalingItem);
  Release(s_scalingMenu);
  for (const auto item : s_emulationItems)
    Release(item);
  Release(s_emulationItem);
  Release(s_emulationMenu);
  Release(s_topItem);
  Release(s_submenu);
  Release(s_target);
  s_startProgramItem = nullptr;
  s_stopProgramItem = nullptr;
  s_startOverlayItem = nullptr;
  s_stopOverlayItem = nullptr;
  s_identifyItem = nullptr;
  s_swapItem = nullptr;
  s_scalingStretchItem = nullptr;
  s_scalingFillItem = nullptr;
  s_scalingFitItem = nullptr;
  s_scalingItem = nullptr;
  s_scalingMenu = nullptr;
  s_emulationItems.fill(nullptr);
  s_emulationItem = nullptr;
  s_emulationMenu = nullptr;
  s_topItem = nullptr;
  s_submenu = nullptr;
  s_target = nullptr;
  s_mainMenu = nullptr;
  s_commands.clear();
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuSetState(bool canStartProgram, bool canStopProgram,
                               bool canStartOverlay, bool canStopOverlay,
                               bool canIdentifyDisplays,
                               bool isTestPatternVisible,
                               bool canSwapOutputs, int scalingMode,
                               int emulationAspect) {
  if (!EnsureRuntime())
    return;
  SendMessage<void>(s_startProgramItem, "setEnabled:",
                    static_cast<signed char>(canStartProgram));
  SendMessage<void>(s_stopProgramItem, "setEnabled:",
                    static_cast<signed char>(canStopProgram));
  SendMessage<void>(s_startOverlayItem, "setEnabled:",
                    static_cast<signed char>(canStartOverlay));
  SendMessage<void>(s_stopOverlayItem, "setEnabled:",
                    static_cast<signed char>(canStopOverlay));
  SendMessage<void>(s_identifyItem, "setEnabled:",
                    static_cast<signed char>(canIdentifyDisplays));
  SendMessage<void>(s_identifyItem, "setState:",
                    static_cast<std::intptr_t>(isTestPatternVisible));
  SendMessage<void>(s_swapItem, "setEnabled:",
                    static_cast<signed char>(canSwapOutputs));
  SendMessage<void>(s_scalingStretchItem, "setState:",
                    static_cast<std::intptr_t>(scalingMode == 0));
  SendMessage<void>(s_scalingFillItem, "setState:",
                    static_cast<std::intptr_t>(scalingMode == 1));
  SendMessage<void>(s_scalingFitItem, "setState:",
                    static_cast<std::intptr_t>(scalingMode == 2));
  for (std::size_t index = 0; index < s_emulationItems.size(); ++index)
    SendMessage<void>(s_emulationItems[index], "setState:",
                      static_cast<std::intptr_t>(
                          emulationAspect == static_cast<int>(index)));
}

extern "C" __attribute__((visibility("default"))) bool
ShitDesignerOutputMenuTryDequeue(int *command) {
  if (command == nullptr || s_commands.empty())
    return false;
  *command = s_commands.front();
  s_commands.pop_front();
  return true;
}

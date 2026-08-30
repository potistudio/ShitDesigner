#include <cstdint>
#include <deque>
#include <dlfcn.h>

namespace {
using NativeObject = void *;
using NativeSelector = void *;
using NativeClass = void *;
using NativeImplementation = void (*)();

enum OutputMenuCommand {
  StartOutput = 0,
  StopOutput = 1,
  IdentifyDisplays = 2
};

static void *s_appKitLibrary;
static void *s_objectiveCLibrary;
static NativeObject s_mainMenu;
static NativeObject s_topItem;
static NativeObject s_submenu;
static NativeObject s_target;
static NativeObject s_startItem;
static NativeObject s_stopItem;
static NativeObject s_identifyItem;
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
  if (tag >= StartOutput && tag <= IdentifyDisplays)
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
  s_startItem = CreateMenuItem("Start External Output", action, StartOutput);
  s_stopItem = CreateMenuItem("Stop External Output", action, StopOutput);
  s_identifyItem =
      CreateMenuItem("Identify Displays", action, IdentifyDisplays);
  for (const auto item : {s_startItem, s_stopItem, s_identifyItem})
    SendMessage<void>(item, "setTarget:", s_target);
  SendMessage<void>(s_submenu, "addItem:", s_startItem);
  SendMessage<void>(s_submenu, "addItem:", s_stopItem);
  const auto separator =
      SendMessage<NativeObject>(s_getClass("NSMenuItem"), "separatorItem");
  SendMessage<void>(s_submenu, "addItem:", separator);
  SendMessage<void>(s_submenu, "addItem:", s_identifyItem);
  s_topItem = CreateMenuItem("Output", nullptr, StartOutput);
  SendMessage<void>(s_topItem, "setSubmenu:", s_submenu);
  SendMessage<void>(s_mainMenu, "addItem:", s_topItem);
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuDestroy(void) {
  if (s_mainMenu != nullptr && s_topItem != nullptr)
    SendMessage<void>(s_mainMenu, "removeItem:", s_topItem);
  Release(s_startItem);
  Release(s_stopItem);
  Release(s_identifyItem);
  Release(s_topItem);
  Release(s_submenu);
  Release(s_target);
  s_startItem = nullptr;
  s_stopItem = nullptr;
  s_identifyItem = nullptr;
  s_topItem = nullptr;
  s_submenu = nullptr;
  s_target = nullptr;
  s_mainMenu = nullptr;
  s_commands.clear();
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuSetState(bool canStart, bool canStop,
                               bool canIdentifyDisplays) {
  if (!EnsureRuntime())
    return;
  SendMessage<void>(s_startItem,
                    "setEnabled:", static_cast<signed char>(canStart));
  SendMessage<void>(s_stopItem,
                    "setEnabled:", static_cast<signed char>(canStop));
  SendMessage<void>(s_identifyItem, "setEnabled:",
                    static_cast<signed char>(canIdentifyDisplays));
}

extern "C" __attribute__((visibility("default"))) bool
ShitDesignerOutputMenuTryDequeue(int *command) {
  if (command == nullptr || s_commands.empty())
    return false;
  *command = s_commands.front();
  s_commands.pop_front();
  return true;
}

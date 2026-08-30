#import <AppKit/AppKit.h>

enum OutputMenuCommand {
  StartOutput = 0,
  StopOutput = 1,
  IdentifyDisplays = 2
};

static NSMutableArray<NSNumber *> *s_commands;
static NSMenuItem *s_topItem;
static NSMenuItem *s_startItem;
static NSMenuItem *s_stopItem;
static NSMenuItem *s_identifyItem;

@interface ShitDesignerOutputMenuTarget : NSObject
- (void)handleOutputMenuItem:(NSMenuItem *)sender;
@end

@implementation ShitDesignerOutputMenuTarget
- (void)handleOutputMenuItem:(NSMenuItem *)sender {
  if (sender.tag < StartOutput || sender.tag > IdentifyDisplays)
    return;
  [s_commands addObject:@(sender.tag)];
}
@end

static ShitDesignerOutputMenuTarget *s_target;

static NSMenuItem *CreateActionItem(NSString *title,
                                    OutputMenuCommand command) {
  NSMenuItem *item =
      [[NSMenuItem alloc] initWithTitle:title
                                 action:@selector(handleOutputMenuItem:)
                          keyEquivalent:@""];
  item.target = s_target;
  item.tag = command;
  return item;
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuCreate(void) {
  if (s_topItem != nil || NSApp.mainMenu == nil)
    return;
  s_commands = [[NSMutableArray alloc] init];
  s_target = [[ShitDesignerOutputMenuTarget alloc] init];
  NSMenu *submenu = [[NSMenu alloc] initWithTitle:@"Output"];
  submenu.autoenablesItems = NO;
  s_startItem = CreateActionItem(@"Start External Output", StartOutput);
  s_stopItem = CreateActionItem(@"Stop External Output", StopOutput);
  s_identifyItem = CreateActionItem(@"Identify Displays", IdentifyDisplays);
  [submenu addItem:s_startItem];
  [submenu addItem:s_stopItem];
  [submenu addItem:[NSMenuItem separatorItem]];
  [submenu addItem:s_identifyItem];
  s_topItem = [[NSMenuItem alloc] initWithTitle:@"Output"
                                         action:nil
                                  keyEquivalent:@""];
  s_topItem.submenu = submenu;
  [NSApp.mainMenu addItem:s_topItem];
#if !__has_feature(objc_arc)
  [submenu release];
#endif
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuDestroy(void) {
  if (s_topItem != nil)
    [NSApp.mainMenu removeItem:s_topItem];
#if !__has_feature(objc_arc)
  [s_startItem release];
  [s_stopItem release];
  [s_identifyItem release];
  [s_topItem release];
  [s_target release];
  [s_commands release];
#endif
  s_startItem = nil;
  s_stopItem = nil;
  s_identifyItem = nil;
  s_topItem = nil;
  s_target = nil;
  s_commands = nil;
}

extern "C" __attribute__((visibility("default"))) void
ShitDesignerOutputMenuSetState(bool canStart, bool canStop,
                               bool canIdentifyDisplays) {
  s_startItem.enabled = canStart;
  s_stopItem.enabled = canStop;
  s_identifyItem.enabled = canIdentifyDisplays;
}

extern "C" __attribute__((visibility("default"))) bool
ShitDesignerOutputMenuTryDequeue(int *command) {
  if (command == nullptr || s_commands.count == 0)
    return false;
  *command = s_commands.firstObject.intValue;
  [s_commands removeObjectAtIndex:0];
  return true;
}

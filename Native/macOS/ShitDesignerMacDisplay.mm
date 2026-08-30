#import <AppKit/AppKit.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>

#include <array>
#include <atomic>
#include <mutex>

#include "IUnityGraphics.h"
#include "IUnityGraphicsMetal.h"

namespace {
constexpr int MaxDisplays = 8;

struct OutputState {
  NSWindow *window = nil;
  CAMetalLayer *layer = nil;
  std::atomic<void *> source{nullptr};
  std::atomic<bool> visible{false};
};

IUnityInterfaces *s_interfaces = nullptr;
IUnityGraphics *s_graphics = nullptr;
IUnityGraphicsMetalV2 *s_metal = nullptr;
id<MTLRenderPipelineState> s_pipeline = nil;
std::array<OutputState *, MaxDisplays> s_outputs{};
std::mutex s_outputsMutex;

bool BuildPipeline() {
  if (s_pipeline != nil || s_metal == nullptr)
    return s_pipeline != nil;
  static const char *shaderSource = R"METAL(
#include <metal_stdlib>
using namespace metal;

struct VertexOutput {
  float4 position [[position]];
  float2 uv;
};

vertex VertexOutput vertex_main(uint vertexId [[vertex_id]]) {
  const float2 positions[3] = {float2(-1.0, -1.0), float2(3.0, -1.0), float2(-1.0, 3.0)};
  const float2 uvs[3] = {float2(0.0, 0.0), float2(2.0, 0.0), float2(0.0, 2.0)};
  VertexOutput output;
  output.position = float4(positions[vertexId], 0.0, 1.0);
  output.uv = uvs[vertexId];
  return output;
}

fragment half4 fragment_main(VertexOutput input [[stage_in]], texture2d<half> source [[texture(0)]]) {
  constexpr sampler textureSampler(mag_filter::linear, min_filter::linear, address::clamp_to_edge);
  return source.sample(textureSampler, input.uv);
}
)METAL";

  @autoreleasepool {
    id<MTLDevice> device = s_metal->MetalDevice();
    NSError *error = nil;
    NSString *source = [NSString stringWithUTF8String:shaderSource];
    id<MTLLibrary> library = [device newLibraryWithSource:source
                                                  options:nil
                                                    error:&error];
    if (library == nil) {
      NSLog(@"ShitDesigner Mac display shader compilation failed: %@", error);
      return false;
    }
    id<MTLFunction> vertex = [library newFunctionWithName:@"vertex_main"];
    id<MTLFunction> fragment = [library newFunctionWithName:@"fragment_main"];
    MTLRenderPipelineDescriptor *descriptor =
        [[MTLRenderPipelineDescriptor alloc] init];
    descriptor.vertexFunction = vertex;
    descriptor.fragmentFunction = fragment;
    descriptor.colorAttachments[0].pixelFormat = MTLPixelFormatBGRA8Unorm_sRGB;
    s_pipeline = [device newRenderPipelineStateWithDescriptor:descriptor
                                                        error:&error];
    if (s_pipeline == nil)
      NSLog(@"ShitDesigner Mac display pipeline creation failed: %@", error);
  }
  return s_pipeline != nil;
}

void OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {
  if (eventType == kUnityGfxDeviceEventInitialize && s_graphics != nullptr &&
      s_graphics->GetRenderer() == kUnityGfxRendererMetal)
    s_metal = s_interfaces->Get<IUnityGraphicsMetalV2>();
  else if (eventType == kUnityGfxDeviceEventShutdown)
    s_metal = nullptr;
}

bool CreateOutputOnMainThread(int displayIndex) {
  if (displayIndex <= 0 || displayIndex >= MaxDisplays || s_metal == nullptr ||
      !BuildPipeline())
    return false;
  NSArray<NSScreen *> *screens = NSScreen.screens;
  if (displayIndex >= static_cast<int>(screens.count))
    return false;

  std::lock_guard<std::mutex> lock(s_outputsMutex);
  if (s_outputs[displayIndex] != nullptr)
    return true;

  NSScreen *screen = screens[displayIndex];
  NSRect frame = screen.frame;
  NSRect contentRect = NSMakeRect(0.0, 0.0, NSWidth(frame), NSHeight(frame));
  NSWindow *window =
      [[NSWindow alloc] initWithContentRect:contentRect
                                  styleMask:NSWindowStyleMaskBorderless
                                    backing:NSBackingStoreBuffered
                                      defer:NO
                                     screen:screen];
  if (window == nil)
    return false;
  window.releasedWhenClosed = NO;
  [window setFrame:frame display:NO];
  window.level = NSNormalWindowLevel;
  window.opaque = YES;
  window.hasShadow = NO;
  window.ignoresMouseEvents = YES;
  window.backgroundColor = NSColor.blackColor;
  window.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces |
                              NSWindowCollectionBehaviorFullScreenAuxiliary;

  NSView *view = [[NSView alloc] initWithFrame:contentRect];
  view.wantsLayer = YES;
  CAMetalLayer *layer = [[CAMetalLayer alloc] init];
  layer.device = s_metal->MetalDevice();
  layer.pixelFormat = MTLPixelFormatBGRA8Unorm_sRGB;
  layer.framebufferOnly = YES;
  layer.opaque = YES;
  layer.displaySyncEnabled = YES;
  layer.maximumDrawableCount = 3;
  layer.frame = view.bounds;
  CGFloat scale = screen.backingScaleFactor;
  layer.contentsScale = scale;
  layer.drawableSize =
      CGSizeMake(NSWidth(contentRect) * scale, NSHeight(contentRect) * scale);
  view.layer = layer;
  window.contentView = view;

  OutputState *state = new OutputState();
  state->window = window;
  state->layer = layer;
  s_outputs[displayIndex] = state;
  return true;
}

void PresentOutput(int displayIndex) {
  CAMetalLayer *layer = nil;
  void *sourcePointer = nullptr;
  {
    std::lock_guard<std::mutex> lock(s_outputsMutex);
    if (displayIndex <= 0 || displayIndex >= MaxDisplays)
      return;
    OutputState *state = s_outputs[displayIndex];
    if (state == nullptr || !state->visible.load())
      return;
    layer = state->layer;
    sourcePointer = state->source.load();
  }

  @autoreleasepool {
    id<CAMetalDrawable> drawable = [layer nextDrawable];
    id<MTLCommandBuffer> commandBuffer =
        s_metal == nullptr ? nil : s_metal->CurrentCommandBuffer();
    if (drawable == nil || commandBuffer == nil)
      return;

    s_metal->EndCurrentCommandEncoder();
    MTLRenderPassDescriptor *descriptor =
        [MTLRenderPassDescriptor renderPassDescriptor];
    descriptor.colorAttachments[0].texture = drawable.texture;
    descriptor.colorAttachments[0].loadAction = MTLLoadActionClear;
    descriptor.colorAttachments[0].storeAction = MTLStoreActionStore;
    descriptor.colorAttachments[0].clearColor = MTLClearColorMake(0, 0, 0, 1);
    id<MTLRenderCommandEncoder> encoder =
        [commandBuffer renderCommandEncoderWithDescriptor:descriptor];
    if (sourcePointer != nullptr) {
      [encoder setRenderPipelineState:s_pipeline];
      [encoder setFragmentTexture:(__bridge id<MTLTexture>)sourcePointer
                          atIndex:0];
      [encoder drawPrimitives:MTLPrimitiveTypeTriangle
                  vertexStart:0
                  vertexCount:3];
    }
    [encoder endEncoding];
    [commandBuffer presentDrawable:drawable];
  }
}
} // namespace

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces *interfaces) {
  s_interfaces = interfaces;
  s_graphics = interfaces->Get<IUnityGraphics>();
  s_graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
  OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
}

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload() {
  if (s_graphics != nullptr)
    s_graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
  s_pipeline = nil;
  s_metal = nullptr;
  s_graphics = nullptr;
  s_interfaces = nullptr;
}

extern "C" UNITY_INTERFACE_EXPORT bool UNITY_INTERFACE_API
ShitDesignerMacDisplayCreate(int displayIndex) {
  __block bool result = false;
  void (^createBlock)(void) = ^{
    result = CreateOutputOnMainThread(displayIndex);
  };
  if (NSThread.isMainThread)
    createBlock();
  else
    dispatch_sync(dispatch_get_main_queue(), createBlock);
  return result;
}

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API
ShitDesignerMacDisplaySetSource(int displayIndex, void *sourceTexture) {
  std::lock_guard<std::mutex> lock(s_outputsMutex);
  if (displayIndex > 0 && displayIndex < MaxDisplays &&
      s_outputs[displayIndex] != nullptr)
    s_outputs[displayIndex]->source.store(sourceTexture);
}

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API
ShitDesignerMacDisplaySetVisible(int displayIndex, bool visible) {
  NSWindow *window = nil;
  {
    std::lock_guard<std::mutex> lock(s_outputsMutex);
    if (displayIndex <= 0 || displayIndex >= MaxDisplays ||
        s_outputs[displayIndex] == nullptr)
      return;
    s_outputs[displayIndex]->visible.store(visible);
    window = s_outputs[displayIndex]->window;
  }
  dispatch_async(dispatch_get_main_queue(), ^{
    if (visible)
      [window orderFrontRegardless];
    else
      [window orderOut:nil];
  });
}

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API
ShitDesignerMacDisplayDestroy(int displayIndex) {
  OutputState *state = nullptr;
  {
    std::lock_guard<std::mutex> lock(s_outputsMutex);
    if (displayIndex <= 0 || displayIndex >= MaxDisplays)
      return;
    state = s_outputs[displayIndex];
    s_outputs[displayIndex] = nullptr;
  }
  if (state == nullptr)
    return;
  state->visible.store(false);
  dispatch_async(dispatch_get_main_queue(), ^{
    [state->window orderOut:nil];
    delete state;
  });
}

extern "C" UNITY_INTERFACE_EXPORT UnityRenderingEvent UNITY_INTERFACE_API
ShitDesignerMacDisplayGetRenderEvent() {
  return PresentOutput;
}

#define WIN32_LEAN_AND_MEAN
#define VK_NO_PROTOTYPES
#include <windows.h>
#include <vulkan/vulkan.h>
#include "../Native/Lighting/lighting_patch.h"
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <vector>
static int checks=0;
static void check(bool ok,const char* description){if(!ok)throw std::runtime_error(description);checks++;}
int main(int argc,char** argv){try{
    VkMemoryBarrier memory{VK_STRUCTURE_TYPE_MEMORY_BARRIER,nullptr,VK_ACCESS_SHADER_WRITE_BIT,VK_ACCESS_SHADER_READ_BIT};
    LightingBarrierPatch off(false,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,1,&memory,0,nullptr);
    check(!off.changed&&off.memory==&memory,"Disabled patch must preserve pointers");
    LightingBarrierPatch patch(true,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,1,&memory,0,nullptr);
    check(patch.changed&&patch.memory!=&memory&&(patch.memory[0].dstAccessMask&VK_ACCESS_INDIRECT_COMMAND_READ_BIT)&&memory.dstAccessMask==VK_ACCESS_SHADER_READ_BIT,"Indirect fix must preserve caller storage");
    LightingBarrierPatch implicit(true,VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,0,nullptr,0,nullptr);
    check(implicit.memoryCount==1&&implicit.memory[0].srcAccessMask==VK_ACCESS_SHADER_WRITE_BIT,"Implicit shader writes must become visible to indirect reads");
    LightingBarrierPatch unrelated(true,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,1,&memory,0,nullptr);
    check(!unrelated.changed,"Unrelated destinations must pass through");
    if(argc>1&&std::strcmp(argv[1],"--math-only")==0){std::printf("PASS: %d lighting math checks (Vulkan device probe skipped).\n",checks);return 0;}
    HMODULE loader=LoadLibraryW(L"vulkan-1.dll");check(loader!=nullptr,"Vulkan loader missing");auto gipa=reinterpret_cast<PFN_vkGetInstanceProcAddr>(GetProcAddress(loader,"vkGetInstanceProcAddr"));
    auto create=reinterpret_cast<PFN_vkCreateInstance>(gipa(nullptr,"vkCreateInstance"));VkInstance instance{};
    const char* layers[]={"VK_LAYER_LAB_lighting"};VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO,nullptr,"LAB isolated lighting probe",1,"LAB",1,VK_API_VERSION_1_1};VkInstanceCreateInfo info{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,nullptr,0,&app,1,layers,0,nullptr};
    check(create(&info,nullptr,&instance)==VK_SUCCESS,"LAB layer instance creation failed");
    auto enumerate=reinterpret_cast<PFN_vkEnumeratePhysicalDevices>(gipa(instance,"vkEnumeratePhysicalDevices"));uint32_t count=0;check(enumerate(instance,&count,nullptr)==VK_SUCCESS&&count>0,"Physical devices unavailable");std::vector<VkPhysicalDevice> physical(count);enumerate(instance,&count,physical.data());
    auto queueProperties=reinterpret_cast<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(gipa(instance,"vkGetPhysicalDeviceQueueFamilyProperties"));count=0;queueProperties(physical[0],&count,nullptr);std::vector<VkQueueFamilyProperties> queues(count);queueProperties(physical[0],&count,queues.data());uint32_t family=0;while(family<count&&!(queues[family].queueFlags&VK_QUEUE_GRAPHICS_BIT))family++;check(family<count,"Graphics queue unavailable");
    float priority=1;VkDeviceQueueCreateInfo qi{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,nullptr,0,family,1,&priority};VkDeviceCreateInfo di{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,nullptr,0,1,&qi,0,nullptr,0,nullptr,nullptr};VkDevice device{};auto createDevice=reinterpret_cast<PFN_vkCreateDevice>(gipa(instance,"vkCreateDevice"));check(createDevice(physical[0],&di,nullptr,&device)==VK_SUCCESS,"LAB layer device forwarding failed");
    auto gdpa=reinterpret_cast<PFN_vkGetDeviceProcAddr>(gipa(instance,"vkGetDeviceProcAddr"));
#define DEVICE(type,name) auto name=reinterpret_cast<type>(gdpa(device,#name));check(name!=nullptr,#name " missing")
    DEVICE(PFN_vkCreateCommandPool,vkCreateCommandPool);DEVICE(PFN_vkAllocateCommandBuffers,vkAllocateCommandBuffers);DEVICE(PFN_vkBeginCommandBuffer,vkBeginCommandBuffer);DEVICE(PFN_vkEndCommandBuffer,vkEndCommandBuffer);DEVICE(PFN_vkCmdPipelineBarrier,vkCmdPipelineBarrier);DEVICE(PFN_vkCmdWaitEvents,vkCmdWaitEvents);DEVICE(PFN_vkCreateEvent,vkCreateEvent);DEVICE(PFN_vkDestroyEvent,vkDestroyEvent);DEVICE(PFN_vkDestroyCommandPool,vkDestroyCommandPool);DEVICE(PFN_vkDestroyDevice,vkDestroyDevice);
    VkCommandPoolCreateInfo pci{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,nullptr,0,family};VkCommandPool pool{};check(vkCreateCommandPool(device,&pci,nullptr,&pool)==VK_SUCCESS,"Command pool failed");VkCommandBufferAllocateInfo ai{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,nullptr,pool,VK_COMMAND_BUFFER_LEVEL_PRIMARY,1};VkCommandBuffer cmd{};check(vkAllocateCommandBuffers(device,&ai,&cmd)==VK_SUCCESS,"Command allocation failed");VkCommandBufferBeginInfo bi{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,nullptr,0,nullptr};check(vkBeginCommandBuffer(cmd,&bi)==VK_SUCCESS,"Command begin failed");
    vkCmdPipelineBarrier(cmd,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,0,0,nullptr,0,nullptr,0,nullptr);
    VkEventCreateInfo ei{VK_STRUCTURE_TYPE_EVENT_CREATE_INFO,nullptr,0};VkEvent event{};check(vkCreateEvent(device,&ei,nullptr,&event)==VK_SUCCESS,"Event creation failed");vkCmdWaitEvents(cmd,1,&event,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_DRAW_INDIRECT_BIT,0,nullptr,0,nullptr,0,nullptr);check(vkEndCommandBuffer(cmd)==VK_SUCCESS,"Command end failed");
    vkDestroyEvent(device,event,nullptr);vkDestroyCommandPool(device,pool,nullptr);vkDestroyDevice(device,nullptr);reinterpret_cast<PFN_vkDestroyInstance>(gipa(instance,"vkDestroyInstance"))(instance,nullptr);FreeLibrary(loader);
    std::printf("PASS: %d lighting math and real Vulkan-loader forwarding checks; no commands submitted to GPU.\n",checks);return 0;
}catch(const std::exception& e){std::fprintf(stderr,"FAIL: %s\n",e.what());return 1;}}

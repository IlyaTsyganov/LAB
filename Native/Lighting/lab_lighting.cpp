#define WIN32_LEAN_AND_MEAN
#define VK_NO_PROTOTYPES
#include <windows.h>
#include <vulkan/vulkan.h>
#include <vulkan/vk_layer.h>
#include <map>
#include <mutex>
#include <memory>
#include <cstring>
#include <string>
#include <atomic>
#include "lighting_patch.h"
#define EXPORT extern "C" __declspec(dllexport)
namespace {
template<class T> void* key(T value){return value?*reinterpret_cast<void**>(value):nullptr;}
struct Instance{VkInstance handle;PFN_vkGetInstanceProcAddr next;PFN_GetPhysicalDeviceProcAddr physical;};
struct Device{VkDevice handle;PFN_vkGetDeviceProcAddr next;PFN_vkCmdPipelineBarrier barrier;PFN_vkCmdWaitEvents wait;bool eligible;};
std::recursive_mutex gate;
std::map<void*,std::shared_ptr<Instance>> instances;
std::map<void*,std::shared_ptr<Device>> devices;
std::atomic<ULONGLONG> nextRead{0};std::atomic<bool> requested{false};
bool supportedImage(){
    auto base=reinterpret_cast<const unsigned char*>(GetModuleHandleW(nullptr));if(!base)return false;
    auto dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<=0||dos->e_lfanew>0x100000)return false;
    auto nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64)return false;
    struct Identity{DWORD timestamp,size,rva;};const Identity audited[]={{0x5e794131,0x1e706000,0x336210},{0x5ec7deca,0x1d551000,0x339b10},{0x5eeb0267,0x1d396000,0x303dc0}};
    const unsigned char prefix[]={0x48,0x89,0x5c,0x24,0x08,0x57,0x48,0x83,0xec,0x20};
    for(auto i:audited)if(nt->FileHeader.TimeDateStamp==i.timestamp&&nt->OptionalHeader.SizeOfImage==i.size&&std::memcmp(base+i.rva,prefix,sizeof(prefix))==0)return true;return false;
}
bool enabled(){
    ULONGLONG now=GetTickCount64(),next=nextRead.load();if(now>=next&&nextRead.compare_exchange_strong(next,now+200)){
        wchar_t opt[4]{};bool value=GetEnvironmentVariableW(L"LAB_LIGHTING_FIX",opt,4)==1&&opt[0]==L'1';
        wchar_t path[32768]{};DWORD n=GetEnvironmentVariableW(L"LAB_DATA_DIRECTORY",path,32768);if(n>0&&n<32768){std::wstring file=std::wstring(path)+L"\\lighting.ini";value=value&&GetPrivateProfileIntW(L"Lighting",L"Enabled",1,file.c_str())!=0;}requested=value;
    }return requested.load();
}
std::shared_ptr<Device> deviceFor(VkCommandBuffer command){std::lock_guard<std::recursive_mutex> lock(gate);auto i=devices.find(key(command));return i==devices.end()?nullptr:i->second;}
}
EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetInstanceProcAddr(VkInstance,const char*);
EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(VkDevice,const char*);
EXPORT VKAPI_ATTR VkResult VKAPI_CALL vkCreateInstance(const VkInstanceCreateInfo* info,const VkAllocationCallbacks* allocation,VkInstance* instance){
    auto chain=reinterpret_cast<const VkLayerInstanceCreateInfo*>(info->pNext);while(chain&&!(chain->sType==VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO&&chain->function==VK_LAYER_LINK_INFO))chain=reinterpret_cast<const VkLayerInstanceCreateInfo*>(chain->pNext);
    if(!chain||!chain->u.pLayerInfo)return VK_ERROR_INITIALIZATION_FAILED;auto link=chain->u.pLayerInfo;auto next=link->pfnNextGetInstanceProcAddr;auto physical=link->pfnNextGetPhysicalDeviceProcAddr;
    auto create=reinterpret_cast<PFN_vkCreateInstance>(next(VK_NULL_HANDLE,"vkCreateInstance"));const_cast<VkLayerInstanceCreateInfo*>(chain)->u.pLayerInfo=link->pNext;
    VkResult result=create(info,allocation,instance);if(result==VK_SUCCESS){std::lock_guard<std::recursive_mutex> lock(gate);instances[key(*instance)]=std::make_shared<Instance>(Instance{*instance,next,physical});}return result;
}
EXPORT VKAPI_ATTR void VKAPI_CALL vkDestroyInstance(VkInstance instance,const VkAllocationCallbacks* allocation){
    std::shared_ptr<Instance> data;{std::lock_guard<std::recursive_mutex> lock(gate);auto i=instances.find(key(instance));if(i==instances.end())return;data=i->second;instances.erase(i);}reinterpret_cast<PFN_vkDestroyInstance>(data->next(instance,"vkDestroyInstance"))(instance,allocation);
}
EXPORT VKAPI_ATTR VkResult VKAPI_CALL vkCreateDevice(VkPhysicalDevice physical,const VkDeviceCreateInfo* info,const VkAllocationCallbacks* allocation,VkDevice* device){
    std::shared_ptr<Instance> owner;{std::lock_guard<std::recursive_mutex> lock(gate);auto i=instances.find(key(physical));if(i==instances.end())return VK_ERROR_INITIALIZATION_FAILED;owner=i->second;}
    auto chain=reinterpret_cast<const VkLayerDeviceCreateInfo*>(info->pNext);while(chain&&!(chain->sType==VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO&&chain->function==VK_LAYER_LINK_INFO))chain=reinterpret_cast<const VkLayerDeviceCreateInfo*>(chain->pNext);if(!chain||!chain->u.pLayerInfo)return VK_ERROR_INITIALIZATION_FAILED;
    auto link=chain->u.pLayerInfo;auto next=link->pfnNextGetDeviceProcAddr;auto create=reinterpret_cast<PFN_vkCreateDevice>(link->pfnNextGetInstanceProcAddr(owner->handle,"vkCreateDevice"));const_cast<VkLayerDeviceCreateInfo*>(chain)->u.pLayerInfo=link->pNext;
    VkResult result=create(physical,info,allocation,device);if(result==VK_SUCCESS){VkPhysicalDeviceProperties properties{};reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(owner->next(owner->handle,"vkGetPhysicalDeviceProperties"))(physical,&properties);
        auto data=std::make_shared<Device>(Device{*device,next,reinterpret_cast<PFN_vkCmdPipelineBarrier>(next(*device,"vkCmdPipelineBarrier")),reinterpret_cast<PFN_vkCmdWaitEvents>(next(*device,"vkCmdWaitEvents")),supportedImage()&&properties.vendorID==0x10de});std::lock_guard<std::recursive_mutex> lock(gate);devices[key(*device)]=data;
    }return result;
}
EXPORT VKAPI_ATTR void VKAPI_CALL vkDestroyDevice(VkDevice device,const VkAllocationCallbacks* allocation){std::shared_ptr<Device> data;{std::lock_guard<std::recursive_mutex> lock(gate);auto i=devices.find(key(device));if(i==devices.end())return;data=i->second;devices.erase(i);}reinterpret_cast<PFN_vkDestroyDevice>(data->next(device,"vkDestroyDevice"))(device,allocation);}
EXPORT VKAPI_ATTR void VKAPI_CALL vkCmdPipelineBarrier(VkCommandBuffer command,VkPipelineStageFlags source,VkPipelineStageFlags destination,VkDependencyFlags flags,uint32_t memoryCount,const VkMemoryBarrier* memory,uint32_t bufferCount,const VkBufferMemoryBarrier* buffers,uint32_t imageCount,const VkImageMemoryBarrier* images){auto data=deviceFor(command);if(!data)return;LightingBarrierPatch patch(data->eligible&&enabled(),source,destination,memoryCount,memory,bufferCount,buffers);data->barrier(command,source,destination,flags,patch.memoryCount,patch.memory,bufferCount,patch.buffers,imageCount,images);}
EXPORT VKAPI_ATTR void VKAPI_CALL vkCmdWaitEvents(VkCommandBuffer command,uint32_t eventCount,const VkEvent* events,VkPipelineStageFlags source,VkPipelineStageFlags destination,uint32_t memoryCount,const VkMemoryBarrier* memory,uint32_t bufferCount,const VkBufferMemoryBarrier* buffers,uint32_t imageCount,const VkImageMemoryBarrier* images){auto data=deviceFor(command);if(!data)return;LightingBarrierPatch patch(data->eligible&&enabled(),source,destination,memoryCount,memory,bufferCount,buffers);data->wait(command,eventCount,events,source,destination,patch.memoryCount,patch.memory,bufferCount,patch.buffers,imageCount,images);}
static PFN_vkVoidFunction interception(const char* name){if(!name)return nullptr;
#define ENTRY(n) if(std::strcmp(name,#n)==0)return reinterpret_cast<PFN_vkVoidFunction>(n);
ENTRY(vkCreateInstance) ENTRY(vkDestroyInstance) ENTRY(vkCreateDevice) ENTRY(vkDestroyDevice) ENTRY(vkGetInstanceProcAddr) ENTRY(vkGetDeviceProcAddr) ENTRY(vkCmdPipelineBarrier) ENTRY(vkCmdWaitEvents)
#undef ENTRY
return nullptr;}
EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetInstanceProcAddr(VkInstance instance,const char* name){if(!instance)return name&&(std::strcmp(name,"vkCreateInstance")==0||std::strcmp(name,"vkGetInstanceProcAddr")==0)?interception(name):nullptr;if(auto f=interception(name))return f;std::lock_guard<std::recursive_mutex> lock(gate);auto i=instances.find(key(instance));return i==instances.end()?nullptr:i->second->next(instance,name);}
EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL vkGetDeviceProcAddr(VkDevice device,const char* name){if(!device)return nullptr;if(auto f=interception(name))return f;std::lock_guard<std::recursive_mutex> lock(gate);auto i=devices.find(key(device));return i==devices.end()?nullptr:i->second->next(device,name);}
static PFN_vkVoidFunction VKAPI_CALL physicalProc(VkInstance instance,const char* name){std::lock_guard<std::recursive_mutex> lock(gate);auto i=instances.find(key(instance));if(i==instances.end())return nullptr;return i->second->physical?i->second->physical(instance,name):i->second->next(instance,name);}
EXPORT VKAPI_ATTR VkResult VKAPI_CALL lab_negotiate_layer_interface_version(VkNegotiateLayerInterface* info){if(!info||info->sType!=LAYER_NEGOTIATE_INTERFACE_STRUCT)return VK_ERROR_INITIALIZATION_FAILED;if(info->loaderLayerInterfaceVersion>2)info->loaderLayerInterfaceVersion=2;info->pfnGetInstanceProcAddr=vkGetInstanceProcAddr;info->pfnGetDeviceProcAddr=vkGetDeviceProcAddr;info->pfnGetPhysicalDeviceProcAddr=physicalProc;return VK_SUCCESS;}

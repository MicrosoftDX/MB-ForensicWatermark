# Introduction 
This solution contains Azure Functions, dotnet core embedder and Watermarking Logic Apps.

Logic apps implement end to end watermark process using Azure  functions to execute specifics action against Azure Media Services account, Azure Storage and Kubernetes cluster.

This tutorial cover create and configuration of Storage, Azure Functions and Logic Apps. Kubernetes clusters and containers creation and configuration  is explained on readme.md on K8S folder. Kubernetes cluster and container image is a prerequisite  of this configuration.

# Getting Started
## Deploying process
### Watermark Storage Account
First of all you have to create a Azure Resource Group and Azure Storage account in it. On these storage you have to create 2 Azure queues on it.
1. **embeddernotification**: this queue receive output notification from watermark embedder process.
2. **preprocessorout**: this queue receive output notification from watermark preprocessor process.

### Azure functions WaterMArkingActions
Azure Functions project called **WaterMArkingActions** implement all Action logic and it is call from Logic APPS process.

Before deploy **WaterMArkingActions** you have to had created Azure Resource Group and Azure Storage on IT 
(previews step). Next, you could deploy functions using right click on project Name Publish.

After success deployed Azure Function, you need to add configuration Application settings.

1. **Storageconn**: Azure Storage connection string of Azure watermark Storage. It use to keep MMRK files and watermark process information.
2. **TenantId**: your Azure AD TENANT DOMAIN
3. **ClientId**: your Azure AD client ID, to connect to Azure Media Services
4. **ClientSecret**: your Azure AD client secret, to connect to Azure Media Services
5. **AMSApiUri**: your Azure Media Services REST API Endpoint
6. **AMSStorageConStr**: Azure Media Services Storage connection string
7. **K8SURL**: Kubernetes cluster REST API endpoint
8. **K8SURLTOKEN**: Kubernetes cluster REST API token
9. **imageName**: container image name of your watermarking container.
10. **K8SJobAggregation**: Number of rendition to put on the first K8S job. for example on a 5 renditions video, if you use 4, 4 lower rendition will go in one K8S job and the highest resolution will go on a separate job.
11. **gopsize**: GOP size
12. **KeepWatermakedBlobs**: keep or not on intermediary blob storage the output MP4 after watermark process, default is false. Only use true for debugging activities. 

After you setup all Function configuration you have to create a **Host Key**. That key will use by Logic Apps process to call all functions, so you will use that Key on Logic App configuration. 


### Logic Apps process
To deploy a Logic APP with visual studio check https://docs.microsoft.com/en-us/azure/logic-apps/logic-apps-deploy-from-vs#deploy-your-logic-app-from-visual-studio

As I mentioned before Logic APPS uses Azure functions to implement same of their actions on the logic process, so you need to have deployed Azure Functions before to configure Logic APPS becausee you need Azure function URL and KEY.

#### Logic Apps PreprocessorB
This process receive messages from queue preprocessorout of Azure watermark Storage. Every time that K8S finish a MMRK file, it send a message to this queue and this process read the message and update Azure watermark Storage tables with MMRK information. 

The process configuration is on PreprocessorB.parameters.json file, and it include
1. **azurequeues_1_storageaccount**: Azure watermark Storage name
2. **azurequeues_1_sharedkey**: azurequeues_1_sharedkey key
3. **MessageSecKeep**: time in seconds to keep message hidden after peek from the queue.
4. **UpdateMMRKStatus**: Azure Function URL with key of Azure Function **UpdateMMRKStatus** 

#### Logic Apps embebbedNotifications
This process receive messages from queue **embeddernotification** of Azure watermark Storage. Every time that K8S finish a new watermarked MP4 copy file, it send a message to this queue and this process read the message and update Azure watermark Storage tables with MP4 information. 

The process configuration is on *embebbedNotifications.parameters.json* file, and it include
1. **azurequeues_1_storageaccount**: Azure watermark Storage name
2. **azurequeues_1_sharedkey**: azurequeues_1_sharedkey key
3. **MessageSecKeep**: time in seconds to keep message hidden after peek from the queue.
4. **UpdateWaterMarkedRender**: Azure Function URL with key of Azure Function **UpdateWaterMarkedRender** 


#### Logic Apps UnifiedProcess
This orchestration process to generate a Azure Media Services watermarked asset copy. 
The process configuration is on *UnifiedProcess.parameters* file, and it include
1. **yourapp**: sub domain of your Azure function URL. For example, on if this is your Function URL https://functionapp666.azurewebsites.net/ yourapp value has to be **functionapp666**
2. **HostKeys**: It is Azure Function Host Key created on Function deployment step.


### Test Deployment 
After you finish deployment process you will have on the same resource group

1. Azure Function 
2. Azure Storage 
3. API Connection
4. UnifiedProcess Logic App
5. embebbedNotifications Logic APP
6. PreprocessorB Logic APP

 So, to test **UnifiedProcess** you need to execute a POST CALL to CallBack URL specifying **AssetId** of original Assset and **EmbebedCodes** list. Each code on the list will produce a new Asset copy on AMS.

```json
POST /workflows/[** your workflowwid here ***]/triggers/manual/paths/invoke?api-version=2016-06-01&amp;sp=%2Ftriggers%2Fmanual%2Frun&amp;sv=1.0&amp;sig=[** your sig **] HTTP/1.1
Host: [*** your HOST HERE ***]
Content-Type: application/json
Cache-Control: no-cache
Postman-Token: abd40c58-d30d-c34c-6165-08161868e0de

{
  "AssetId": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
  "EmbebedCodes": 
    [
    	"0x1ADE29"
    ]

}
```
As response you will receive JOB status information.

```json
{
    "AssetStatus": {
        "AssetId": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
        "State": "Running"
    },
    "JobStatus": {
        "JobID": "08586993026535557503409978660",
        "State": "Running",
        "Details": "Queue",
        "StartTime": "2017-08-09T18:43:54.0835878+00:00",
        "FinishTime": null,
        "Duration": null,
        "EmbebedCodeList": [
            "0x1ADE29"
        ]
    },
    "EmbebedCodesList": [
        {
            "EmbebedCodeValue": "0x1ADE29",
            "State": "Running",
            "ParentAssetID": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
            "AssetID": "",
            "Details": "Just Start"
        }
    ]
}
```

After you trigger the JOB, you could check JOB status calling Azure Function **GetUnifiedProcessStatus** endpoint using same **AssetId** and **JobID** from preview response.
```json
POST /api/GetUnifiedProcessStatus?code=[** your code here **] HTTP/1.1
Host: [*** your HOST HERE ***]
Content-Type: application/json
Cache-Control: no-cache
Postman-Token: b9994858-53f0-c98d-02a0-370059a56a03

{
	"AssetId": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
	"JobID": "08586993026535557503409978660"
}
```




# Introduction 
This solution contains Azure Functions, dotnet core embedder and Watermarking Logic Apps.

Logic apps implement end to end watermark process using Azure  functions to execute specifics action against Azure Media Services account, Azure Storage and Kubernetes cluster.

This tutorial cover create and configuration of Storage, Azure Functions and Logic Apps. Kubernetes clusters and containers creation and configuration  is explained on readme.md on K8S folder. Kubernetes cluster and container image is a prerequisite  of this configuration.

# Getting Started
## Deploying process


### Step 1: Azure functions WaterMArkingActions
Azure Functions project called **WaterMArkingActions** implement all Action logic and it is call from Logic APPS process.

Deploy is base on use Azure Resource manager templates. you could see more information about templates <a href="https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-template-deploy" target="_blank">https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-template-deploy</a>


<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoftDX%2FMB-ForensicWatermark%2Fmaster%2FMB-ForensicWatermark%2FWaterMarking%2FAzureFunctionActions.json" target="_blank">![](http://azuredeploy.net/deploybutton.png)</a>

Template **<a href="https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/master/MB-ForensicWatermark/WaterMarking/AzureFunctionActions.json" target="_blank">AzureFunctionActions.json</a>** will deploy:


a. Azure Storage Account
b. Azure Hosting Plan
c. Azure Function


Templates parameters are:

* **functionAppName*: Azure function name, it has to be unique
* **TenantId**: your Azure AD TENANT DOMAIN
* **TenantId**: your Azure AD TENANT DOMAIN
* **ClientId**: your Azure AD client ID, to connect to Azure Media Services
* **ClientSecret**: your Azure AD client secret, to connect to Azure Media Services
* **AMSApiUri**: your Azure Media Services REST API Endpoint
* **AMSStorageConStr**: Azure Media Services Storage connection string
* **K8SURL**: Kubernetes cluster REST API endpoint
* **K8SURLTOKEN**: Kubernetes cluster REST API token
* **imageName**: container image name of your watermarking container.
* **K8SJobAggregation**: Number of rendition to put on the first K8S job. for example on a 5 renditions video, if you use 4, 4 lower rendition will go in one K8S job and the highest resolution will go on a separate job.
* **REPOURL**: Github code repository. Use your own Fork, default URL is https://github.com/MicrosoftDX/MB-ForensicWatermark.git

### Step 2: Generate Azure Function Host Key

After deploy this first template you have to create a Azure Function Host key. 

**Host keys** are shared by all functions within the function app. When used as an API key, these allow access to any function within the function app. More information about <a href="https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-http-webhook#working-with-keys" target="_blank">Azure Function Host Key </a>.

That Host key and Function's name are parameters for the next step. 


### Step 3: Logic Apps UnifiedProcess

This orchestration process generate a Azure Media Services watermarked asset copy.

To deploy this Logic Apps you need to use ARM template  file <a href="https://github.com/MicrosoftDX/MB-ForensicWatermark/blob/master/MB-ForensicWatermark/WaterMarking/UnifiedProcess.json" target="_blank">**UnifiedProcess.json**</a> on same resource group that you used on first step or next Deployment blue button.

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoftDX%2FMB-ForensicWatermark%2Fmaster%2FMB-ForensicWatermark%2FWaterMarking%2FUnifiedProcess.json" target="_blank">![](http://azuredeploy.net/deploybutton.png)</a>

Template parameters are

* **yourapp**: sub domain of your Azure function URL. For example, on if this is your Function URL https://functionapp666.azurewebsites.net/ yourapp value has to be **functionapp666**
* **HostKeys**: It is Azure Function Host Key created on Function deployment step.




### Final Step: Test Deployment 
After you finish deployment process you will have on the same resource group

1.  Azure Storage
2. Azure App Service plan
3. Azure Function
4. UnifiedProcess Logic App


So, to test **UnifiedProcess** you need to execute a POST CALL to CallBack URL specifying **AssetId** of original Assset and **EmbeddedCodes** list. Each code on the list will produce a new Asset copy on AMS.

```json
POST /workflows/[** your workflowwid here ***]/triggers/manual/paths/invoke?api-version=2016-06-01&amp;sp=%2Ftriggers%2Fmanual%2Frun&amp;sv=1.0&amp;sig=[** your sig **] HTTP/1.1
Host: [*** your HOST HERE ***]
Content-Type: application/json
Cache-Control: no-cache
Postman-Token: abd40c58-d30d-c34c-6165-08161868e0de

{
  "AssetId": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
  "EmbeddedCodes": 
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
        "JobId": "08586993026535557503409978660",
        "State": "Running",
        "Details": "Queue",
        "StartTime": "2017-08-09T18:43:54.0835878+00:00",
        "FinishTime": null,
        "Duration": null,
        "EmbeddedCodeList": [
            "0x1ADE29"
        ]
    },
    "EmbeddedCodesList": [
        {
            "EmbeddedCodeValue": "0x1ADE29",
            "State": "Running",
            "ParentAssetId": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
            "AssetID": "",
            "Details": "Just Start"
        }
    ]
}
```

After you trigger the JOB, you could check JOB status calling Azure Function **GetUnifiedProcessStatus** endpoint using same **AssetId** and **JobId** from preview response.
```json
POST /api/GetUnifiedProcessStatus?code=[** your code here **] HTTP/1.1
Host: [*** your HOST HERE ***]
Content-Type: application/json
Cache-Control: no-cache
Postman-Token: b9994858-53f0-c98d-02a0-370059a56a03

{
	"AssetId": "nb:cid:UUID:ecda4e79-f800-44de-9fd5-562de140c7c7",
	"JobId": "08586993026535557503409978660"
}
```

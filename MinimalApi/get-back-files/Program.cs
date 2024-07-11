using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

string cloudinaryCloudName = builder.Configuration["Cloudinary:CloudName"];
string cloudinaryApiKey = builder.Configuration["Cloudinary:ApiKey"];
string cloudinaryApiSecret = builder.Configuration["Cloudinary:ApiSecret"];
string cosmosDbConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
string cosmosDbDatabaseName = builder.Configuration["CosmosDb:DatabaseName"];
string cosmosDbContainerName = builder.Configuration["CosmosDb:ContainerName"];

Cloudinary cloudinary = new Cloudinary(new Account(cloudinaryCloudName, cloudinaryApiKey, cloudinaryApiSecret));
CosmosClient cosmosClient = new CosmosClient(cosmosDbConnectionString);
Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync(cosmosDbDatabaseName);
Container container = await database.CreateContainerIfNotExistsAsync(cosmosDbContainerName, "/id");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


//an API that uses IFormFile to push a file to database and cloudinary.
app.MapPost("/upload", async (IFormFile file) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("File is required.");
    }

    await using var stream = file.OpenReadStream();
    var compressedImage = await CompressImageAsync(stream);

    var uploadParams = new ImageUploadParams()
    {
        File = new FileDescription(file.FileName, new MemoryStream(compressedImage)),
        PublicId = Guid.NewGuid().ToString()
    };

    var uploadResult = await cloudinary.UploadAsync(uploadParams);

    var filePath = uploadResult.SecureUrl.AbsoluteUri;
    var fileRecord = new { id = uploadParams.PublicId, filePath = filePath };

    await container.CreateItemAsync(fileRecord);

    return Results.Ok(new { id = fileRecord.id, filePath = fileRecord.filePath });
}).DisableAntiforgery();

// Helper method to compress the image
async Task<byte[]> CompressImageAsync(Stream inputStream)
{
    using var image = await Image.LoadAsync(inputStream);
    image.Mutate(x => x.Resize(new ResizeOptions
    {
        Size = new SixLabors.ImageSharp.Size(800, 600), // Adjust size as needed
        Mode = ResizeMode.Max
    }));
    using var outputStream = new MemoryStream();
    await image.SaveAsJpegAsync(outputStream);
    return outputStream.ToArray();
}

app.MapGet("/file/{id}", async (string id) =>
{
    try
    {
        var response = await container.ReadItemAsync<dynamic>(id, new PartitionKey(id));
        var filePath = response.Resource.filePath;

        return Results.Ok(new { filePath = filePath });
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound("File not found.");
    }
});

app.UseHttpsRedirection();
app.Run();

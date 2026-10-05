namespace LOL_GameApi
{
    /// <summary>程序入口：配置应用启动环境与生命周期。</summary>
    public class Program
    {
        /// <summary>初始化应用运行环境并启动程序入口。</summary>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // 注册业务服务（DataDragon 版本查询与缓存）
            builder.Services.AddSingleton<LOL_GameApi.Services.DataDragonService>();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // 全局异常处理中间件（统一错误响应）
            app.UseMiddleware<LOL_GameApi.Middleware.ExceptionHandlingMiddleware>();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
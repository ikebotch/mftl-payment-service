namespace MftlPaymentService.Models.v1;

public class ServiceResponseModel<T>
{
    public string Status { get; set; }
    public string Message { get; set; }
    public T Data { get; set; }
    public PaginationModel? Pagination { get; set; }
}

public class ServiceResponseModel
{
    public string Status { get; set; }
    public string Message { get; set; }
}

public class PaginationModel
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Limit { get; set; }
}
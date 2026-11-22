Run on Windows with .NET 8:

```powershell
dotnet run --project tests/AblRegression/AblRegression.csproj
```

The executable checks legacy JSON and estimates, exact 1%/10%/25% curves,
sparse APL interpolation, boundaries, monotonicity, save/load, and clone isolation.
It also runs the shipped compute shaders on D3D11 WARP against synthetic pixels,
checking nonlinear per-pixel output statistics and the pointer probe. It does not
capture the desktop or modify application settings. It does not validate a physical
display's EOTF or hardware performance at full resolution.

PQ editor unit tests verify bidirectional conversion and reference values. To
also check all 256 pairs from the reference workbook, pass its path:

```powershell
dotnet run --project tests/AblRegression/AblRegression.csproj -- "D:\path\EOTF_Fig.xlsx"
```

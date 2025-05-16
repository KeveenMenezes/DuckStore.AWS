import { inject, Injectable } from '@angular/core';
import { Product } from '../../shared/models/product';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Pagination } from '../../shared/models/pagination';

@Injectable({
  providedIn: 'root',
})
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = 'catalog-service/';

  brands: string[] = [];
  types: string[] = [];

  getProductPagination(pageIndex: number, pageSize: number) {
    return this.http.get<Pagination<Product>>(
      `${this.baseUrl}/products?pageIndex=${pageIndex}&pageSize=${pageSize}`
    );
  }

  getProducts(brands?: string[], types?: string[], sort?: string) {
    const params = new HttpParams({
      fromObject: {
        ...(brands?.length ? { brands: brands.join(',') } : {}),
        ...(types?.length ? { types: types.join(',') } : {}),
        ...(sort ? { sort } : {}),
        pageSize: 20,
      },
    });

    return this.http.get<Pagination<Product>>(`${this.baseUrl}products`, {
      params,
    });
  }

  getBrands() {
    if (this.brands.length > 0) return;

    return this.http.get<string[]>(`${this.baseUrl}/brands`).subscribe({
      next: (response) => (this.brands = response),
    });
  }

  getTypes() {
    if (this.types.length > 0) return;

    return this.http.get<string[]>(`${this.baseUrl}/types`).subscribe({
      next: (response) => (this.types = response),
    });
  }
}

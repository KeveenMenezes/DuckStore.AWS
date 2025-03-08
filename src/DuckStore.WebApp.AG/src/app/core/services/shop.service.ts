import { inject, Injectable } from '@angular/core';
import { Product } from '../../shared/models/product';
import { HttpClient } from '@angular/common/http';
import { Pagination } from '../../shared/models/pagination';

@Injectable({
  providedIn: 'root',
})
export class ShopService {
  private readonly baseUrl = 'https://localhost:5050/api/';
  private readonly http = inject(HttpClient);

  getProductPagination(pageIndex: number, pageSize: number) {
    return this.http.get<Pagination<Product>>(
      `${this.baseUrl}products/${pageIndex}/${pageSize}`
    );
  }
}

import { Component, inject, OnInit } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Product } from '../../shared/models/product';
import { ProductItemComponent } from './product-item/product-item.component';
import { CatalogService } from '../../core/services/catalog.service';
import { FiltersDialogComponent } from './filters-dialog/filters-dialog.component';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';

@Component({
  selector: 'app-shop',
  standalone: true,
  imports: [ProductItemComponent, MatButton, MatIcon],
  templateUrl: './shop.component.html',
  styleUrl: './shop.component.scss',
})
export class ShopComponent implements OnInit {
  private readonly catalogService = inject(CatalogService);
  private readonly matDialogService = inject(MatDialog);

  pageIndex = 1;
  pageSize = 10;
  products: Product[] = [];
  selectedBrands: string[] = [];
  selectedTypes: string[] = [];

  ngOnInit(): void {
    this.initializeShop();
  }

  initializeShop() {
    this.catalogService.getBrands();
    this.catalogService.getTypes();
    this.catalogService
      .getProductPagination(this.pageIndex, this.pageSize)
      .subscribe({
        next: (response) => {
          this.products = response.items;
        },
        error: (error: any) => {
          console.log(error);
        },
      });
  }

  openFiltersDialog() {
    const dialogRef = this.matDialogService.open(FiltersDialogComponent, {
      minWidth: '500px',
      data: {
        selectedBrands: this.selectedBrands,
        selectedTypes: this.selectedTypes
      }
    });
    dialogRef.afterClosed().subscribe({
      next: result => {
        if(result){
          console.log(result);
          this.selectedBrands = result.selectedBrands
          this.selectedTypes = result.selectedTypes
        }
      }
    })
  }
}

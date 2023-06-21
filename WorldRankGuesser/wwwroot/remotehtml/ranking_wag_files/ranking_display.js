$(document).ready(function() {
  $('#rankingAAGymnast').DataTable( {
      "scrollCollapse": true,
      "responsive":     true,
      "ordering":       true,
      "paging":         false,
      "info":           false,
      "searching":      false,
  } );

  $('#rankingAACountry').DataTable( {
      "scrollY":        "400px",
      "scrollCollapse": true,
      "responsive":     true,
      "ordering":       true,
      "paging":         false,
      "info":           false,
      "searching":      false,
  } );

  $('#rankingApp').DataTable( {
      "scrollCollapse": true,
      "responsive":     true,
      "ordering":       true,
      "paging":         false,
      "info":           false,
      "searching":      false,
  } );

  $('table.table').DataTable( {
      "scrollCollapse": true,
      "responsive":     true,
      "ordering":       true,
      "paging":         false,
      "info":           false,
      "searching":      false,
  } );
});
